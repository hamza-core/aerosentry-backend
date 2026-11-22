"""
AeroSentry ingest worker.

What it does, in one paragraph:
    Every few seconds it asks the backend "which walls must I watch?" (GET worker/get-jobs).
    For each wall it opens the camera stream (via the media server, or the camera directly),
    reads the video, keeps ONLY EVERY 5TH FRAME (the rate the ML model was trained on) and posts
    that frame to the backend (POST worker/send-frame). The backend calls the ML service, works
    out the wall cell and stores the alarm. This program never talks to the ML service itself.

Run (from this folder, in the environment that has opencv and requests):
    WORKER_KEY=<same value as Worker:ApiKey in appsettings.json> python worker.py

Everything is local-network only. No internet is needed.
"""

import argparse
import logging
import os
import sys
import threading
import time

import cv2
import requests

log = logging.getLogger("worker")


# ---------------------------------------------------------------------------
# One thread per wall: read the stream, keep every Nth frame, send it
# ---------------------------------------------------------------------------
class StreamWatcher(threading.Thread):
    def __init__(self, job, source, args):
        super().__init__(daemon=True, name="wall-" + job["wallName"])
        self.job = job
        self.source = source
        self.args = args
        self.stop_event = threading.Event()
        self.finished = False          # a video FILE that reached its end (not restarted unless --loop)
        self._busy = threading.Lock()  # held while one frame is being sent
        self.sent = 0
        self.dropped = 0

    def stop(self):
        self.stop_event.set()

    def run(self):
        is_file = os.path.exists(self.source)
        backoff = 2
        while not self.stop_event.is_set():
            cap = cv2.VideoCapture(self.source)
            if not cap.isOpened():
                log.warning("[%s] cannot open the stream, retrying in %ds", self.job["wallName"], backoff)
                self.stop_event.wait(backoff)
                backoff = min(backoff * 2, 30)
                continue

            backoff = 2
            fps = cap.get(cv2.CAP_PROP_FPS) or 25.0
            log.info("[%s] watching (%s, %.1f fps, one frame in %d)", self.job["wallName"],
                     "file" if is_file else "stream", fps, self.args.stride)

            frame_number = 0
            started = time.time()
            while not self.stop_event.is_set():
                ok, frame = cap.read()
                if not ok:
                    break
                frame_number += 1

                # A file is read as fast as the disk allows; slow it down to real time like a camera.
                if is_file:
                    due = started + frame_number / fps
                    time.sleep(max(0.0, due - time.time()))

                if frame_number % self.args.stride != 0:
                    continue

                # Never let a slow answer pile frames up: if the last frame is still being sent, skip this one.
                if not self._busy.acquire(blocking=False):
                    self.dropped += 1
                    continue

                jpeg = cv2.imencode(".jpg", frame)[1].tobytes()
                timestamp = frame_number / fps if is_file else time.time() - started
                threading.Thread(target=self._send, args=(jpeg, frame_number, timestamp), daemon=True).start()

            cap.release()

            if is_file and not self.args.loop:
                log.info("[%s] end of file (%d frames sent, %d dropped)", self.job["wallName"], self.sent, self.dropped)
                self.finished = True
                return
            if not self.stop_event.is_set():
                log.warning("[%s] stream ended, reconnecting", self.job["wallName"])
                self.stop_event.wait(2)

    def _send(self, jpeg, frame_number, timestamp):
        try:
            response = requests.post(
                self.args.backend + "/api/v1/worker/send-frame",
                params={"wallId": self.job["wallId"], "frameIndex": frame_number, "timestampSeconds": round(timestamp, 3)},
                files={"frame": ("frame.jpg", jpeg, "image/jpeg")},
                headers={"X-Worker-Key": self.args.key},
                timeout=30,
            )
            if response.status_code == 200:
                self.sent += 1
                result = response.json()
                if result.get("alarmRaised"):
                    log.warning("[%s] ALARM  probability=%.2f  cell=%s  event=%s", self.job["wallName"],
                                result["probability"], result.get("cellLabel") or "outside every cell", result.get("eventId"))
            elif response.status_code == 409:
                log.info("[%s] the wall was switched off", self.job["wallName"])
            else:
                log.error("[%s] backend answered %d: %s", self.job["wallName"], response.status_code, response.text[:200])
        except requests.RequestException as exc:
            log.error("[%s] backend not reachable: %s", self.job["wallName"], exc)
        finally:
            self._busy.release()


# ---------------------------------------------------------------------------
# The manager: keep one watcher per wall that the backend wants watched
# ---------------------------------------------------------------------------
def fetch_jobs(args):
    response = requests.get(args.backend + "/api/v1/worker/get-jobs", headers={"X-Worker-Key": args.key}, timeout=10)
    if response.status_code == 401:
        log.error("The backend refused the worker key (WORKER_KEY must equal Worker:ApiKey in appsettings.json)")
        sys.exit(1)
    response.raise_for_status()
    return response.json()


def choose_source(job, args):
    """Which address to open: a test override, the camera itself, or the media server (default)."""
    if job["wallId"] in args.overrides:
        return args.overrides[job["wallId"]]
    return job["cameraSourceUrl"] if args.source == "camera" else job["mediaSourceUrl"]


def main():
    parser = argparse.ArgumentParser(description="AeroSentry ingest worker")
    parser.add_argument("--backend", default=os.environ.get("BACKEND_URL", "http://localhost:5078"))
    parser.add_argument("--key", default=os.environ.get("WORKER_KEY", ""), help="or set WORKER_KEY")
    parser.add_argument("--source", choices=["media", "camera"], default="media",
                        help="media = read the stream from the media server (default); camera = read the camera directly")
    parser.add_argument("--stride", type=int, default=5, help="keep one frame in N (the model was trained on 5)")
    parser.add_argument("--poll", type=float, default=5.0, help="seconds between asking the backend for jobs")
    parser.add_argument("--override", action="append", default=[], metavar="WALLID=SOURCE",
                        help="for tests: read this video file or URL instead, for one wall (repeatable)")
    parser.add_argument("--loop", action="store_true", help="restart a video file when it ends")
    parser.add_argument("--run-seconds", type=float, default=0, help="for tests: exit after this many seconds")
    args = parser.parse_args()

    if not args.key:
        sys.exit("Set the worker key: WORKER_KEY=... or --key ...")
    args.overrides = dict(item.split("=", 1) for item in args.override)

    logging.basicConfig(level=logging.INFO, format="%(asctime)s  %(levelname)-7s %(message)s", datefmt="%H:%M:%S")
    log.info("Ingest worker started. Backend: %s  Source: %s  Stride: %d", args.backend, args.source, args.stride)

    watchers = {}   # wallId -> StreamWatcher
    started = time.time()
    try:
        while True:
            try:
                jobs = {job["wallId"]: job for job in fetch_jobs(args)}
            except requests.RequestException as exc:
                log.error("Cannot get jobs from the backend: %s", exc)
                jobs = None

            if jobs is not None:
                # Stop watching walls that were switched off.
                for wall_id in list(watchers):
                    if wall_id not in jobs:
                        log.info("Stop watching %s", watchers[wall_id].job["wallName"])
                        watchers.pop(wall_id).stop()

                # Start (or restart) watching walls that must be watched.
                for wall_id, job in jobs.items():
                    watcher = watchers.get(wall_id)
                    if watcher is not None and (watcher.is_alive() or watcher.finished):
                        continue
                    source = choose_source(job, args)
                    shown = source if os.path.exists(source) else source.split("@")[-1]   # never print passwords
                    log.info("Start watching %s from %s", job["wallName"], shown)
                    watchers[wall_id] = StreamWatcher(job, source, args)
                    watchers[wall_id].start()

            if args.run_seconds and time.time() - started > args.run_seconds:
                break
            time.sleep(args.poll)
    except KeyboardInterrupt:
        pass
    finally:
        for watcher in watchers.values():
            watcher.stop()
        log.info("Ingest worker stopped")


if __name__ == "__main__":
    main()
