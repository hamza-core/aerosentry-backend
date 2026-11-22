# Ingest worker

A small Python program that reads the camera streams and feeds the backend. It is **not** part of the
frozen ML service, and it never talks to the ML service: it only posts frames to the backend.

```
Camera -> MediaMTX --RTSP--> ingest worker --(1 frame in 5, JPEG)--> backend -> ML service
                  \--HLS--> the apps (viewing)                          \-> wall cell + stored alarm
```

## What it needs
- Python with `opencv-python` and `requests` (the `cv_env` conda environment already has both; see `requirements.txt`).
- The backend running, and its worker key. The key lives in `AeroSentry.Api/appsettings.json` under `Worker:ApiKey`.
- No internet.

## Run it
```bash
cd ingest-worker
export WORKER_KEY="<the value of Worker:ApiKey>"
python worker.py                      # reads streams from MediaMTX (rtsp://localhost:8554/cam-<cameraId>)
python worker.py --source camera      # reads each camera directly (no media server)
```

It asks the backend every 5 seconds which walls are switched on (`detection/start` switches a wall on,
`detection/stop` switches it off), and starts or stops watching them by itself.

## Options
| Option | Meaning |
|---|---|
| `--source media\|camera` | where to read the video (default `media`) |
| `--stride 5` | keep one frame in N. The model was trained on 5, do not change it |
| `--poll 5` | seconds between asking for jobs |
| `--backend URL` | default `http://localhost:5078` (or env `BACKEND_URL`) |
| `--override WALLID=FILE_OR_URL` | test with a video file instead of a camera |
| `--loop` | restart a test video file when it ends |

## Why every 5th frame, here and not in the ML service
In live mode the ML service treats **every frame it receives as one sampled frame**. The worker therefore does
the sampling, on the camera's real frame rate, exactly like the model's training data (1 frame in 5).
If the backend or the ML service is slower than the stream, the worker drops frames instead of falling behind.
