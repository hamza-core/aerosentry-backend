"""
AeroSentry ingest worker.

What it does, in one paragraph:
    Every few seconds it asks the backend "which walls must I watch?" (GET worker/get-jobs).
    For each wall it opens the camera stream (via the media server, or the camera directly), reads
    the video and keeps frames at a steady rate in REAL TIME (6 per second by default, see below), and
    posts each kept frame to the backend (POST worker/send-frame). The backend calls the ML service,
    works out the wall cell and stores the alarm. This program never talks to the ML service itself.

Why 5 frames per second, whatever the camera's frame rate:
    The model was trained on one frame in every 5 of the dataset's videos. Measured on the training set
    (wall-intrusion-detection/dataset/train, 703 videos): 65% are 25 fps, 20% are 30 fps, 14% are 24 fps, so that is
    5.0 samples per second of real time for the typical clip (4.8 to 6.0 for the others, 5.2 on average).
    A fixed "every 5th frame" gives a 15 fps phone only 3 per second and a 60 fps camera 12. So frames are chosen by
    TIME, and any camera gives about 5 per second. A camera slower than 5 fps simply has every frame used.

What the ML service can really do:
    In live mode it re-runs its whole trailing window (up to 60 frames) through the bidirectional LSTM on every frame.
    Measured on the development Mac: about 5.6 frames/s at first, falling to about 2-2.5 frames/s once the window is full.
    So the worker PACES itself: it measures how long a frame takes (backend + ML) and spaces the frames evenly at the
    fastest rate that can be kept up, never above the sample rate. It logs a warning when that is below the trained rate,
    because the model then sees frames further apart than in training.

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

# Read RTSP over TCP and give up on a dead camera after 10 s. By default OpenCV tries UDP, which loses packets on Wi-Fi
# (measured with a phone: lost keyframes, broken frames). Must be set BEFORE opencv is imported. A value already set wins.
os.environ.setdefault("OPENCV_FFMPEG_CAPTURE_OPTIONS", "rtsp_transport;tcp|stimeout;10000000")

import cv2
import requests

log = logging.getLogger("worker")

# Frames are spaced PACE_MARGIN x the measured time one frame takes (backend + ML), so a slightly slower answer does not
# collide with the next frame. Higher = more even spacing, lower rate.
PACE_MARGIN = 1.25


# ---------------------------------------------------------------------------
# One thread per wall: read the stream, keep frames at the sample rate, send them
# ---------------------------------------------------------------------------
class StreamWatcher(threading.Thread):
    def __init__(self, job, source, args):
        super().__init__(daemon=True, name="wall-" + job["wallName"])
        self.job = job
        self.name_ = job["wallName"]
        self.source = source
        self.args = args
        self.stop_event = threading.Event()
        self.finished = False          # a video FILE that reached its end (not restarted unless --loop)
        self._busy = threading.Lock()  # held while one frame is being sent
        self._preview_busy = threading.Lock()   # held while one preview picture is being uploaded
        self._preview_error_at = 0.0   # when a failed preview upload was last reported (do not flood the log)
        self.sent = 0
        self.dropped = 0
        self.alarms = 0
        self.round_trip = None         # smoothed seconds per frame (backend + ML), measured on every send

    def stop(self):
        self.stop_event.set()

    def run(self):
        is_file = os.path.exists(self.source)
        base_interval = 1.0 / self.args.sample_rate
        backoff = 2
        while not self.stop_event.is_set():
            cap = cv2.VideoCapture(self.source)
            if not cap.isOpened():
                log.warning("[%s] cannot open the stream, retrying in %ds", self.name_, backoff)
                self.stop_event.wait(backoff)
                backoff = min(backoff * 2, 30)
                continue

            backoff = 2
            file_fps = (cap.get(cv2.CAP_PROP_FPS) or 25.0) if is_file else None
            log.info("[%s] watching a %s, keeping %.1f frames per second", self.name_,
                     "file" if is_file else "live stream", self.args.sample_rate)

            frame_number = 0
            started = time.monotonic()
            next_due = None                   # the time (in seconds) the next frame must be kept
            seen = 0                          # frames the camera delivered since the last report
            sent_mark, dropped_mark = self.sent, self.dropped
            report_mark = time.monotonic()
            shape_checked = False
            warned_slow = False
            warned_ml = False

            while not self.stop_event.is_set():
                ok, frame = cap.read()
                if not ok:
                    break
                frame_number += 1
                seen += 1

                if is_file:
                    # A file is read as fast as the disk allows; slow it to real time like a camera.
                    t = frame_number / file_fps
                    time.sleep(max(0.0, started + t - time.monotonic()))
                else:
                    # A live camera: the moment the frame arrived (cap.read() waits for the next frame).
                    t = time.monotonic() - started

                if not shape_checked:
                    shape_checked = True
                    self._check_shape(frame)

                if next_due is None:
                    next_due = t
                if t >= next_due:
                    # Pace: never faster than the sample rate, and never faster than a frame takes to be analysed.
                    interval = max(base_interval, PACE_MARGIN * (self.round_trip or 0.0))
                    next_due += interval
                    if t - next_due > 2 * interval:
                        next_due = t + interval   # the stream stalled for a while: do not burst to catch up

                    # Never let a slow answer pile frames up: if the last frame is still being sent, skip this one.
                    if self._busy.acquire(blocking=False):
                        jpeg = cv2.imencode(".jpg", frame)[1].tobytes()
                        timestamp = t
                        threading.Thread(target=self._send, args=(jpeg, frame_number, timestamp, frame), daemon=True).start()
                    else:
                        self.dropped += 1

                now = time.monotonic()
                if now - report_mark >= self.args.report_seconds:
                    span = now - report_mark
                    sent_now, dropped_now = self.sent - sent_mark, self.dropped - dropped_mark
                    camera_fps = seen / span
                    log.info("[%s] camera %.1f fps | sent %.1f/s | dropped %d | analysis %.0f ms/frame | alarms so far %d", self.name_,
                             camera_fps, sent_now / span, dropped_now, (self.round_trip or 0) * 1000, self.alarms)
                    if self.round_trip and self.round_trip * PACE_MARGIN > base_interval * 1.3 and not warned_ml:
                        log.warning("[%s] analysing a frame takes %.0f ms, so only about %.1f frames/s can be kept up "
                                    "(the model was trained at about %.0f/s). Detection works, but on frames further apart "
                                    "than in training. A faster ML machine removes this.", self.name_,
                                    self.round_trip * 1000, 1 / (self.round_trip * PACE_MARGIN), self.args.sample_rate)
                        warned_ml = True
                    if camera_fps < self.args.sample_rate * 0.9 and not warned_slow and not is_file:
                        log.warning("[%s] the camera delivers only %.1f fps, below the sample rate %.1f: every frame is used",
                                    self.name_, camera_fps, self.args.sample_rate)
                        warned_slow = True
                    if dropped_now > 0.2 * (sent_now + dropped_now) and sent_now + dropped_now > 5:
                        log.warning("[%s] %d of %d frames were skipped because the backend or the ML service is too slow",
                                    self.name_, dropped_now, sent_now + dropped_now)
                    seen, sent_mark, dropped_mark, report_mark = 0, self.sent, self.dropped, now

            cap.release()

            if is_file and not self.args.loop:
                log.info("[%s] end of file (%d frames sent, %d dropped)", self.name_, self.sent, self.dropped)
                self.finished = True
                return
            if not self.stop_event.is_set():
                log.warning("[%s] stream ended, reconnecting", self.name_)
                self.stop_event.wait(2)

    def _check_shape(self, frame):
        """The annotation is a fraction of the picture, so the shape (not the size) must match."""
        height, width = frame.shape[:2]
        annotated_w, annotated_h = self.job.get("frameWidth") or 0, self.job.get("frameHeight") or 0
        if annotated_w and annotated_h:
            real, wanted = width / height, annotated_w / annotated_h
            if abs(real - wanted) / wanted > 0.02:
                log.warning("[%s] the camera sends %dx%d but the wall was annotated on %dx%d: the wall lines will not line up. "
                            "Annotate the wall again on this camera's picture.", self.name_, width, height, annotated_w, annotated_h)

    def _send(self, jpeg, frame_number, timestamp, frame=None):
        began = time.monotonic()
        try:
            response = requests.post(
                self.args.backend + "/api/v1/worker/send-frame",
                params={"wallId": self.job["wallId"], "frameIndex": frame_number, "timestampSeconds": round(timestamp, 3)},
                files={"frame": ("frame.jpg", jpeg, "image/jpeg")},
                headers={"X-Worker-Key": self.args.key},
                timeout=30,
            )
            took = time.monotonic() - began
            self.round_trip = took if self.round_trip is None else 0.7 * self.round_trip + 0.3 * took
            if response.status_code == 200:
                self.sent += 1
                result = response.json()
                if frame is not None and self.args.preview:
                    # Draw and upload on another thread, so the preview never slows the analysis down.
                    threading.Thread(target=self._publish_preview, args=(frame, result), daemon=True).start()
                if result.get("alarmRaised"):
                    self.alarms += 1
                    log.warning("[%s] ALARM  probability=%.2f  cell=%s  %s", self.name_, result["probability"],
                                result.get("cellLabel") or "outside every cell", result.get("incidentCode") or "")
            elif response.status_code == 409:
                log.info("[%s] the wall was switched off", self.name_)
            else:
                log.error("[%s] backend answered %d: %s", self.name_, response.status_code, response.text[:200])
        except requests.RequestException as exc:
            log.error("[%s] backend not reachable: %s", self.name_, exc)
        finally:
            self._busy.release()


    # --- the live preview: the camera picture with the people the model found drawn on it --------------------------
    def _draw(self, frame, result):
        """The picture the apps show: a box round every person (orange; RED while the climbing alarm is on), with the
        model's confidence and the wall cell the person's feet are in. Sent about 640 px wide to keep it small."""
        height, width = frame.shape[:2]
        scale = min(1.0, 640.0 / width)
        picture = cv2.resize(frame, (int(width * scale), int(height * scale))) if scale < 1.0 else frame.copy()
        h, w = picture.shape[:2]
        alarm = bool(result.get("alarmActive"))
        colour = (0, 0, 255) if alarm else (0, 165, 255)          # BGR: red / orange

        for person in result.get("people") or []:
            box = person.get("box") or []
            if len(box) != 4:
                continue
            x1, y1, x2, y2 = int(box[0] * w), int(box[1] * h), int(box[2] * w), int(box[3] * h)
            cv2.rectangle(picture, (x1, y1), (x2, y2), colour, 3 if alarm else 2)
            cell = person.get("cellLabel")
            label = "Person %d%%" % round((person.get("confidence") or 0) * 100) + (" - " + cell if cell else "")
            (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)
            top = max(y1 - th - 8, 0)
            cv2.rectangle(picture, (x1, top), (x1 + tw + 8, top + th + 8), colour, -1)
            cv2.putText(picture, label, (x1 + 4, top + th + 2), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1, cv2.LINE_AA)
            # The foot point: the spot the backend uses to decide which cell the person is in.
            cv2.circle(picture, (int(person.get("footX", 0) * w), int(person.get("footY", 0) * h)), 4, colour, -1)

        if alarm:
            # Only the word: the model's reading changes from frame to frame and can dip while the alarm is still on,
            # so a number here would contradict the percentage of the alert itself.
            text = "ALARM"
            (tw, th), _ = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, 0.7, 2)
            cv2.rectangle(picture, (0, 0), (tw + 20, th + 18), (0, 0, 255), -1)
            cv2.putText(picture, text, (10, th + 8), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2, cv2.LINE_AA)
        return cv2.imencode(".jpg", picture, [cv2.IMWRITE_JPEG_QUALITY, 70])[1].tobytes()

    def _publish_preview(self, frame, result):
        # One upload at a time: if the last one is still going, this picture is simply skipped.
        if not self._preview_busy.acquire(blocking=False):
            return
        try:
            requests.post(
                self.args.backend + "/api/v1/worker/post-preview",
                params={"wallId": self.job["wallId"]},
                files={"frame": ("preview.jpg", self._draw(frame, result), "image/jpeg")},
                headers={"X-Worker-Key": self.args.key},
                timeout=3,
            ).raise_for_status()
        except requests.RequestException as exc:
            now = time.monotonic()
            if now - self._preview_error_at > 60:          # report at most once a minute
                self._preview_error_at = now
                log.warning("[%s] the live preview could not be sent: %s (detection itself is not affected)", self.name_, exc)
        finally:
            self._preview_busy.release()


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
    parser.add_argument("--sample-rate", type=float, default=5.0,
                        help="most frames per second to analyse, in real time. The model was trained on about 5 (one in 5 of mostly 25 fps). "
                             "The worker goes slower by itself when the ML service cannot keep up")
    parser.add_argument("--poll", type=float, default=5.0, help="seconds between asking the backend for jobs")
    parser.add_argument("--report-seconds", type=float, default=30.0, help="how often to print the rate report")
    parser.add_argument("--override", action="append", default=[], metavar="WALLID=SOURCE",
                        help="for tests: read this video file or URL instead, for one wall (repeatable)")
    parser.add_argument("--loop", action="store_true", help="restart a video file when it ends")
    parser.add_argument("--no-preview", dest="preview", action="store_false",
                        help="do not send the live preview picture (boxes drawn on the camera picture) to the backend")
    parser.add_argument("--run-seconds", type=float, default=0, help="for tests: exit after this many seconds")
    args = parser.parse_args()

    if not args.key:
        sys.exit("Set the worker key: WORKER_KEY=... or --key ...")
    if not 0.5 <= args.sample_rate <= 30:
        sys.exit("--sample-rate must be between 0.5 and 30")
    args.overrides = dict(item.split("=", 1) for item in args.override)

    logging.basicConfig(level=logging.INFO, format="%(asctime)s  %(levelname)-7s %(message)s", datefmt="%H:%M:%S")
    log.info("Ingest worker started. Backend: %s  Source: %s  Sample rate: %.1f frames/s", args.backend, args.source, args.sample_rate)

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
                        log.info("Stop watching %s", watchers[wall_id].name_)
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
