# Future work and decisions still open

What is NOT built yet, and what must be decided before it can be. Update this file as items are done.

## Feature 8: Drone (not implemented, waiting for the drone hardware to be stable)

### What exists today
- Placeholder functions under `/api/v1/drones/...` (see `DronesController.cs` and the "Planned" table in `docs/API.md`).
  They all answer **501 Not Implemented**, so the apps can show "not available yet".
- Everything the drone will need already exists in the backend: an alert knows its wall and cell, a cell knows its
  laser route (ordered grid squares), and the grid knows its square size.

### What is known (from the design screens and the plan)
- The drone side is a **Python script on the Raspberry Pi** (with a Pixhawk flight controller), GPS-denied, in a separate repo.
- Registry: id and name. The design also shows model ("Quad X"), battery %, status ("Docked and ready"), home cell (e.g. 9,9) and a preflight check.
- The first drone in the list responds to incidents.
- Mission screen shows: intruder location (wall / cell), wall range, drone current cell, next target cell, route progress,
  navigation (Autonomous / Manual), laser feedback (Stable / Lost), tracking state (Acquired 91% / Searching).
- Manual control screen shows: altitude, throttle, yaw, pitch/roll, ground speed, drone camera, virtual joysticks.
- Status values named in the design: docked and ready, navigating, arrived, laser link lost, target lost (searching), manual override, landing.

### Decisions still open (do not guess; decide with the drone team)
1. **How the Pi talks to the backend.** Options: (a) the Pi polls over HTTP (simplest, ~200 ms latency, only the `requests`
   library on the Pi); (b) the Pi keeps a WebSocket open (lowest latency, more code on the Pi); (c) the backend calls a small
   server on the Pi (needs the Pi's address to stay fixed). Recommended: (a) first.
2. **Authentication of a drone.** Suggested: one secret key per drone, created when the drone is registered (like the ingest worker's key).
3. **What the backend sends to the Pi for a mission.** Suggested: wall name, cell label, the route as ordered grid squares
   `[{row, col}]`, the grid square size, and the home cell.
4. **What the Pi reports (telemetry), and how often.** Suggested: state, battery, current cell (row, col), index of the next target
   square, laser feedback, tracking state and confidence, altitude, ground speed, throttle / yaw / pitch / roll. About 2 per second.
5. **Commands to the Pi:** dispatch mission, return home, emergency land, take manual control, release manual control, and the
   joystick values (about 10 per second while in manual control).
6. **Dispatch:** manual (a person presses "send drone" on an alert) first; automatic response (the 85% policy in the design) with
   the settings feature.
7. **Drone camera:** the Pi would publish its camera to the media server (MediaMTX) and the backend would return a playback URL.
8. **A simulated drone script** (follows the route, reports telemetry, obeys the commands, with one marked place to plug in the
   Pixhawk code) so the backend can be tested without hardware. Not written yet.

### Planned backend pieces
`Drone` and `FlightLog` tables, a drone service, status pushed to the apps over SignalR (`DroneStatusChanged`), flight logs per mission.

## ML service speed: a decision for you and your supervisor

**Finding.** In live mode the ML service re-runs its whole 60-frame window through the bidirectional LSTM on every frame.
Measured on the development Mac, one frame at a time, 478x850 JPEG, ML service alone:

| | frames 1-20 | frames 21-60 | window full (61+) |
|---|---|---|---|
| ML service as approved (GPU / `mps`) | 177-362 ms | 359-403 ms | 422-513 ms = **2.0-2.4 frames/s** |
| Same code forced to CPU | 236 ms (warm-up) | 77 ms | 72 ms = **about 14 frames/s** |

The model was trained at about **5 samples per second** (see `ingest-worker/README.md`). On the GPU the live detection therefore
runs at about half the trained rate or less; with several walls it is shared between them. Through the backend the speed is the
same as the ML service alone (the backend adds almost nothing).

**Same answers on the CPU.** A throwaway copy of the ML folder with the device forced to CPU was compared with the original on 7 clips
(1 clip of your own plus 6 from the test set, crossing and normal): 292 frames, largest probability difference 0.00000 (the service rounds to 4
decimals), alarms on exactly the same frames, same decisions, same people and boxes. The original ML folder was not modified.

**What would change.** Two lines: in `backend/app/services/ml_inference.py` the function `_select_device()` should return the CPU, and in
`extract_every_5_frames.py` the line `DEVICE = "mps" if ... else "cpu"` should be `"cpu"`. The model files and all logic stay the same.
Real phone (30 fps) measured against the CPU copy: analysed at 4.5 to 5.0 frames/s at about 95 ms per frame, instead of 1.2 to 3.1.

**What was done.** The ML service is frozen and approved by the supervisor, so the original (GPU) folder was NOT changed. A **copy** with those two lines
changed now sits next to it: `/Users/ab/FINAL FYP AI MODEL/standalone_every_5_frames_cpu` (see its `CPU_VERSION.md`; the model files are byte-identical).
`ml-service/start-ml.sh cpu|gpu|stop|status` switches between them (one at a time, both use port 8010). Which one the demo uses, and whether the
supervisor must be told about the CPU copy, is your decision.
Not measured: other Macs, a machine with an NVIDIA GPU (the code only knows `mps` and `cpu`), or two or more walls at once.

## Tested with a real IP Webcam phone
`192.168.0.107:8080` ("IP Webcam Server 0.4"): no login, `/shot.jpg` 640x480 in about 0.3 s, `/video` MJPEG at about 30 fps, and RTSP
(`h264.sdp`, H.264 video only; `h264_ulaw/pcm/aac.sdp` add audio). Checked: backend camera test, snapshot, worker reading the phone directly
(`--source camera`), **MediaMTX pulling the phone over RTSP/TCP and serving RTSP and HLS** (one connection to the phone for several readers,
HLS playlist and fMP4 segments, decoded by ffmpeg, config hot-reload), the worker reading it through MediaMTX, ML analysis and an alert.
See `media/README.md` (UDP loses packets over Wi-Fi and breaks HLS; use `rtspTransport: tcp`).
The annotation used in that test was a placeholder, so its alarm cell ("outside every cell") means nothing. Still untested:
phone login enabled, phone on weak Wi-Fi for hours, phone sleeping, and your CCTV cameras and NVR (RTSP paths differ by brand).

## Other things not done yet

| Item | Note |
|---|---|
| Merge repeated alarms | Today one ML alarm = one alert. A repeat on the same wall within a time window could join the same incident. Decide after seeing real footage. |
| CCTV cameras and the NVR | Not tested. Needs their brand and RTSP paths, then a block per camera in `media/mediamtx.yml` (name `cam-<cameraId>`, `rtspTransport: tcp`). Writing that file by hand is the manual step: the backend could generate it from the cameras (not built). |
| IP Webcam (phone) through MediaMTX | Tested directly (`--source camera`). MediaMTX needs the phone's RTSP mode or ffmpeg (not installed on this Mac). Not tested. |
| Snapshot for RTSP / ONVIF cameras | `cameras/get-snapshot` only works for IP Webcam phones (the backend cannot decode RTSP without ffmpeg). For the others the app captures a frame from the playback stream. |
| Frame rate | Done: sampled by time at 5/s (the measured training rate), paced to what the ML service can analyse. Open: ML service speed, see the section above. |
| Feature 9: authorized people (face ID) | Not started. Face service is a separate ML service. |
| Feature 10: reports and settings | Not started. Includes the automatic response policy and alert notification preferences shown in the design. |
| Alert notifications outside the app | Push notifications to phones are not built (SignalR only works while the app is open). |
| Design change | Add an "Annotate wall" screen (2 top points, 2 bottom points on the camera frame) before cell marking. |
| Secrets in git | `appsettings.json` holds the SQL password, JWT key and worker key and is deliberately not committed. For a team, move them to `dotnet user-secrets` or `.env`. |
| Interfaces for old services | `AuthService`, `GuardService`, `PropertyService` have no interfaces yet (new services do). |
| CLAUDE.md end-of-feature duties | `docs/HOW-IT-WORKS.md` sections, `docs/OFFLINE-CHECKLIST.md` updates, and the three "change it yourself" exercises are not written for Features 3 to 7. |
| Tests | Checked with scripts run by hand, not an automated test project. |
| Production hardening | No rate limiting, no HTTPS setup for the LAN demo, CORS origins are the local dev ones. |
