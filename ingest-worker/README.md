# Ingest worker

A small Python program that reads the camera streams and feeds the backend. It is **not** part of the
frozen ML service, and it never talks to the ML service: it only posts frames to the backend.

```
Camera -> MediaMTX --RTSP--> ingest worker --(about 5 JPEG frames per second)--> backend -> ML service
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

## Reading RTSP
The worker reads RTSP over **TCP** and gives up on a dead camera after 10 s (it then reconnects). OpenCV's default is UDP, which lost packets over
Wi-Fi in testing. Set `OPENCV_FFMPEG_CAPTURE_OPTIONS` yourself to change that. The media server and its camera config are in `../media/`.

## Options
| Option | Meaning |
|---|---|
| `--source media\|camera` | where to read the video (default `media`) |
| `--sample-rate 5` | most frames per second to analyse (real time). Default 5 = the training rate; see below |
| `--poll 5` | seconds between asking for jobs |
| `--backend URL` | default `http://localhost:5078` (or env `BACKEND_URL`) |
| `--override WALLID=FILE_OR_URL` | test with a video file instead of a camera |
| `--loop` | restart a test video file when it ends |

## Frame rate: how it works for any camera
- **Sampled by time, not by counting frames.** The worker keeps about **5 frames per second of real time**, whether the
  camera sends 15, 25, 30 or 60 fps (or a changing rate, like a phone on weak Wi-Fi). A camera slower than 5 fps has every frame used.
- **Why 5.** The model was trained on one frame in every 5 of the dataset's videos. Measured on the training set
  (`wall-intrusion-detection/dataset/train`, 703 videos): 65% are 25 fps, 20% are 30 fps, 14% are 24 fps, which is
  5.0 samples per second for the typical clip (4.8 to 6.0 for the others, 5.2 on average).
- **The ML service is the limit, not the camera.** In live mode it re-runs its whole 60-frame window through the
  bidirectional LSTM on every frame. Measured on the development Mac it handles about 5.6 frames/s at first and only about
  2 to 2.5 frames/s once the window is full. So the worker measures the time per frame and **paces itself**: even spacing at
  the fastest rate that can be kept up, never above `--sample-rate`. It prints `analysis NNN ms/frame` in its report and a
  warning when the achievable rate is below the trained one (the model then sees frames further apart than in training).
- **More walls = less speed each.** One ML process serves every wall, so the per-wall rate drops as walls are added.
- If a frame is still being analysed when the next is due, the new one is skipped (counted as `dropped`) instead of queuing.
- The first report line of a run includes the start-up phase (the first frames are fast); read the later lines.
