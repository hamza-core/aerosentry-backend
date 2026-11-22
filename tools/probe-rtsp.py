"""
Find the RTSP address of a camera or NVR.

CCTV makers use different RTSP paths, and the exact one is in the camera's manual or web page. This tool tries the common
patterns on ONE host that you name and tells you which ones deliver video. It is read-only: it only asks for a few frames.
The patterns are guesses to try, not facts about your camera: if none opens, look the path up in the camera/NVR settings.

Run (in an environment with opencv, e.g. the cv_env conda environment):
    python tools/probe-rtsp.py 192.168.0.50 --user admin --password 'secret'
    python tools/probe-rtsp.py 192.168.0.107 --port 8080            # an IP Webcam phone
    python tools/probe-rtsp.py 192.168.0.50 --path /my/own/path     # also try a path you know
"""

import argparse
import os
import socket
import sys
import threading
from urllib.parse import quote

# RTSP over TCP (UDP loses packets over Wi-Fi) and a 4 second network timeout. Must be set before opencv is imported.
os.environ["OPENCV_FFMPEG_CAPTURE_OPTIONS"] = "rtsp_transport;tcp|stimeout;4000000"
import cv2  # noqa: E402

# (path, what it usually is). Main streams are high resolution; sub streams are smaller and lighter to analyse.
CANDIDATES = [
    ("/Streaming/Channels/101", "Hikvision main stream, channel 1"),
    ("/Streaming/Channels/102", "Hikvision sub stream, channel 1"),
    ("/cam/realmonitor?channel=1&subtype=0", "Dahua main stream, channel 1"),
    ("/cam/realmonitor?channel=1&subtype=1", "Dahua sub stream, channel 1"),
    ("/h264Preview_01_main", "Reolink main stream"),
    ("/h264Preview_01_sub", "Reolink sub stream"),
    ("/stream1", "TP-Link Tapo and many others, main stream"),
    ("/stream2", "TP-Link Tapo and many others, sub stream"),
    ("/axis-media/media.amp", "Axis"),
    ("/live/ch0", "generic"),
    ("/live/ch00_0", "generic"),
    ("/live", "generic"),
    ("/h264", "generic"),
    ("/video1", "generic"),
    ("/cam1/h264", "generic"),
    ("/11", "generic (some DVRs, main)"),
    ("/12", "generic (some DVRs, sub)"),
    ("/onvif1", "generic ONVIF-style"),
    ("/user=admin_password=_channel=1_stream=0.sdp?real_stream", "generic Chinese DVR/NVR, fill in the real login"),
    ("/h264.sdp", "IP Webcam phone, video only"),
    ("/h264_ulaw.sdp", "IP Webcam phone, with audio"),
]


def tcp_open(host, port):
    try:
        socket.create_connection((host, port), timeout=3).close()
        return True
    except OSError:
        return False


def try_open(url, seconds=10):
    """Open the stream and read 15 frames, with a watchdog so a silent camera cannot hang the tool."""
    result = {}

    def run():
        cap = cv2.VideoCapture(url, cv2.CAP_FFMPEG)
        if not cap.isOpened():
            result["r"] = None
            return
        got, size = 0, None
        for _ in range(15):
            ok, frame = cap.read()
            if ok:
                got += 1
                size = (frame.shape[1], frame.shape[0])
        fps = cap.get(cv2.CAP_PROP_FPS)
        cap.release()
        result["r"] = (got, size, fps) if got else None

    thread = threading.Thread(target=run, daemon=True)
    thread.start()
    thread.join(seconds)
    return result.get("r")


def main():
    parser = argparse.ArgumentParser(description="Find the RTSP address of a camera or NVR")
    parser.add_argument("host", help="the camera's or NVR's IP address, e.g. 192.168.0.50")
    parser.add_argument("--port", type=int, default=554, help="RTSP port (default 554; IP Webcam phones use 8080)")
    parser.add_argument("--user", default="", help="the camera's username")
    parser.add_argument("--password", default="", help="the camera's password")
    parser.add_argument("--path", action="append", default=[], help="an extra path to try, e.g. /my/path (repeatable)")
    args = parser.parse_args()

    print(f"Checking {args.host}:{args.port} ...")
    if not tcp_open(args.host, args.port):
        sys.exit(f"Nothing answers on {args.host}:{args.port}. Is the address right, the camera on, on the same network, "
                 f"and RTSP switched on in its settings? (Most cameras use port 554.)")
    print("The port is open. Trying the common paths (this takes a little while)...\n")

    login = ""
    if args.user:
        login = quote(args.user, safe="") + (":" + quote(args.password, safe="") if args.password else "") + "@"

    found = []
    for path, note in [(p, "your path") for p in args.path] + CANDIDATES:
        shown = f"rtsp://{args.host}:{args.port}{path}"
        outcome = try_open(f"rtsp://{login}{args.host}:{args.port}{path}")
        if outcome:
            got, size, fps = outcome
            print(f"  OPENS   {path:<58} {size[0]}x{size[1]}  {fps:.0f} fps   ({note})")
            found.append((path, size, note))
        else:
            print(f"  -       {path}")

    if not found:
        print("\nNo common path delivered video. Things to check: the username and password (a wrong login looks like 'cannot open'),\n"
              "that RTSP is enabled in the camera/NVR settings, and the exact path in the manual or the camera's web page.\n"
              "For an NVR, each camera is a different channel in the path.")
        sys.exit(1)

    best = found[0]
    print("\nIf several open, prefer the SUB stream (smaller picture) for analysis: it is lighter to decode and as good for finding people.")
    print("\n=== Use it in the app (cameras/create) ===")
    print(f"  sourceType: Rtsp   host: {args.host}   port: {args.port}   streamPath: {best[0]}"
          + (f"   username: {args.user}   password: (yours)" if args.user else ""))
    print("\n=== And in media/mediamtx.yml (name = cam-<the camera's id from the API>) ===")
    print("  cam-<cameraId>:")
    print(f"    source: rtsp://{login}{args.host}:{args.port}{best[0]}")
    print("    rtspTransport: tcp")
    print("    sourceOnDemand: yes")


if __name__ == "__main__":
    main()
