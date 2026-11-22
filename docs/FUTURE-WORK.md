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

## Other things not done yet

| Item | Note |
|---|---|
| Merge repeated alarms | Today one ML alarm = one alert. A repeat on the same wall within a time window could join the same incident. Decide after seeing real footage. |
| Real cameras, NVR and MediaMTX | Only tested with a video file. Needs the exact RTSP URLs of the NVR and cameras, and a `mediamtx.yml` entry per camera (path name `cam-<cameraId>`). |
| IP Webcam (phone) | Serves MJPEG over HTTP. The worker can read it directly with `--source camera`; MediaMTX needs RTSP mode or ffmpeg. Not tested. |
| Frame rate | The model was trained on 1 frame in 5 of 30 fps (about 6 per second). A camera at a different frame rate changes what the model sees. Not tuned. |
| Feature 9: authorized people (face ID) | Not started. Face service is a separate ML service. |
| Feature 10: reports and settings | Not started. Includes the automatic response policy and alert notification preferences shown in the design. |
| Alert notifications outside the app | Push notifications to phones are not built (SignalR only works while the app is open). |
| Design change | Add an "Annotate wall" screen (2 top points, 2 bottom points on the camera frame) before cell marking. |
| Secrets in git | `appsettings.json` holds the SQL password, JWT key and worker key and is deliberately not committed. For a team, move them to `dotnet user-secrets` or `.env`. |
| Interfaces for old services | `AuthService`, `GuardService`, `PropertyService` have no interfaces yet (new services do). |
| CLAUDE.md end-of-feature duties | `docs/HOW-IT-WORKS.md` sections, `docs/OFFLINE-CHECKLIST.md` updates, and the three "change it yourself" exercises are not written for Features 3 to 7. |
| Tests | Checked with scripts run by hand, not an automated test project. |
| Production hardening | No rate limiting, no HTTPS setup for the LAN demo, CORS origins are the local dev ones. |
