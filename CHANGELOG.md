# Changelog

## Unreleased

### Added
- Optional `note` (up to 500 characters) on a property: create, update, get and list. New nullable column `Properties.Note` (migration `AddPropertyNotes`).

### Removed
- The leftover ASP.NET template `WeatherForecast` controller and model. It was a public endpoint (`/WeatherForecast`) that returned random sample data and nothing used it.

### API naming: function-style endpoints
- Every endpoint is now `/api/v1/<feature>/<action>` (e.g. `cameras/create`, `walls/save-cells`); ids moved from the path to the query string (`?propertyId=&cameraId=`). Full list in `docs/API.md`.
- Guards endpoints from Feature 1 changed accordingly: `guards/create`, `guards/list`, `guards/set-status`, `guards/delete`. Auth endpoints did not change.

### Refactor: interfaces in their own folder
- The 11 service interfaces moved from `Services/` to `Services/Interfaces/` (one interface per file). The exceptions and small data types that used to sit inside four of those files now have their own files next to the service (`MlModels.cs`, `FaceModels.cs`, `PersonModels.cs`, `CameraSnapshotException.cs`). No behaviour change. As everywhere in the project the namespace follows the folder, so the interfaces are now in `AeroSentry.Api.Services.Interfaces` and the 23 files that use one got `using AeroSentry.Api.Services.Interfaces;`.

### Documentation
- Complete documentation set: `README.md`, `CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, `docs/DATABASE.md` (generated from the live database), `docs/HOW-IT-WORKS.md` (every feature), `docs/TESTING.md`; `docs/API.md` checked against all 73 endpoints. Checked by a script: no file, endpoint, setting or class name in the documents is wrong.

### Feature 9: Authorized people (face ID)
- `face-service/` (FastAPI + InsightFace `buffalo_l`, offline) and `people/enroll`, `check-photo`, `list`, `get`, `get-photo`, `update`, `disable-access`, `enable-access`, `delete`, `identify` (admin only).
- Enrolment takes 3 photos (front, left, right): exactly one face each, at least 100 px, the three must be the same person; duplicates give 409. Matching threshold 0.45.
- Alarms: faces are looked for in the alarm frame and ~4 earlier ones. A trusted authorized person (face at least 60 px, the same person in at least 2 frames, every person at the wall cleared) is named on the alert (`recognizedPerson`) and the alert gets `autoResponseSkipped` (no automatic drone), policy `Face:AuthorizedPolicy` = `SkipDrone` (default) or `LabelOnly`. The alert itself is never suppressed.
- `alerts/confirm-authorized` (guard or admin, final, in the history), `alerts/get-snapshot` (the alarm picture, for the guard to see who it is). New alert status `ConfirmedAuthorized`.
- Found and fixed while testing: one face could clear two overlapping people; each face now belongs to exactly one person.
- Finding: faces on wall-camera footage are 13 to 50 px wide, too small for reliable recognition. See `docs/FUTURE-WORK.md`.

### Annotation, IP Webcam and frame rate
- `GET cameras/get-snapshot` (picture from an IP Webcam phone, with its size) and `PUT walls/save-annotation`; a changed annotation resets the running ML session.
- Ingest worker now samples by time at 5 frames/s (measured training rate: 65% of training videos are 25 fps, 20% are 30 fps, 14% are 24 fps), paces itself to what the ML service can analyse, and warns when the camera's picture shape differs from the annotation. Details in `ingest-worker/README.md`.
- Tested against a real IP Webcam phone (640x480, about 30 fps, no login): snapshot, stream and the whole detection chain.
- Media server: `media/` (MediaMTX config template and notes). Verified with the real phone: RTSP over TCP pull, RTSP and HLS out, one connection to the phone, config hot-reload. The worker reads RTSP over TCP.
- `ml-service/start-ml.sh cpu|gpu|stop|status`: switch between the original (GPU) ML service and a new CPU copy (`standalone_every_5_frames_cpu`, two lines changed, original untouched). `tools/probe-rtsp.py` finds a camera's RTSP address. `docs/REAL-CAMERAS.md` is the checklist for real cameras.
- Finding: the ML service on the GPU (mps) analyses only about 2-2.5 frames/s once its 60-frame window is full; on the CPU about 14. See `docs/FUTURE-WORK.md`.

### Feature 8: Drone (planned, NOT implemented)
- Placeholder `drones/...` functions answer 501 so the apps can prepare. Open decisions and the full list of unfinished work: `docs/FUTURE-WORK.md`.

### Feature 7: Alert actions
- `POST alerts/acknowledge`, `alerts/mark-false-alarm`, `alerts/close`, `alerts/escalate` (admin only) and `GET alerts/list-actions`.
- Rules in `AlertTransitions` (FalseAlarm and Closed are final); status changes are compare-and-set, so simultaneous presses give one 200 and 409s.
- `AlertAction` table keeps the history (who, what, when, note). Status changes are pushed over SignalR as `AlertUpdated`.
- Suspending or deleting a guard now closes their open SignalR connection; connections also close when the token expires.

### Feature 6: Alerts and real-time
- Every stored detection alarm creates an `Alert` (incident number from a database IDENTITY column, `INC-0001`...).
- Alerts log: `GET /api/v1/alerts/list` and `GET /api/v1/alerts/get`.
- Live push over SignalR at `/hubs/alerts` (message `AlertRaised`) to the property's owner and its active assigned guards.
- JWT accepted as `?access_token=` on `/hubs` paths; CORS added (`Cors:Origins`).
- Ingest worker (`ingest-worker/`) and `worker/get-jobs`, `worker/send-frame`; watching a wall is now saved in the database.

### Feature 2 to 5: properties, cameras, laser grids and walls, ML detection bridge (in progress on feature/cameras)
- Properties with guard assignment, cameras with connection test, laser grids with configurable size, walls with annotation, cells and laser routes, and the detection bridge to the ML service with stored alarm events.

### Feature 1: Auth and roles (merged from feature/auth)
- Added `POST /api/v1/auth/register` (public admin sign-up, password hashed with PasswordHasher).
- Added `POST /api/v1/auth/login` (JWT access token with `sub`, `email`, `role`; 401 for bad credentials, 403 for suspended guards).
- Added `GET /api/v1/auth/me` (requires a valid token).
- Added Admin-only guard management: `POST /api/v1/guards`, `GET /api/v1/guards?status=`, `PATCH /api/v1/guards/{id}/status`, `DELETE /api/v1/guards/{id}`.
- Tokens of suspended or deleted users are rejected (checked on every request).
- Added optional `phoneNumber` on users.
- Added `Users` table (unique index on `Email`) and two migrations.
- Added Scalar API page at `/scalar` (Development only).

### Feature 0: Setup
- Scaffolded `AeroSentry.Api` (.NET 11), SQL Server 2022 in Docker, EF Core.
