# AeroSentry API: endpoint list

Base URL: `http://<server>:5078/api/v1`  (Scalar page: `/scalar`, Development only)

## How every endpoint is named

`/api/v1/<feature>/<action>`

- The path says **what the function does**: `cameras/create`, `walls/save-cells`, `detection/start`.
- **Ids always go in the query string**: `?propertyId=...&cameraId=...`.
- The **JSON body holds only data** (the thing being created or changed).
- HTTP method: `GET` reads, `POST` creates or runs a command, `PUT` updates, `PATCH` changes one field, `DELETE` removes.
- Auth: `Authorization: Bearer <token>` on everything except `auth/register` and `auth/login`.
- Roles: **Admin** can do everything on their own properties. **Guard** can only read (and only assigned properties).
- Errors are ProblemDetails JSON: 400 bad input, 401 no/invalid token, 403 wrong role, 404 not found (or not yours), 409 conflict, 503 ML service down.
- Times are UTC ISO-8601. Coordinates on camera frames are normalised 0 to 1.

## Auth
| Function | Method and path | Who |
|---|---|---|
| Register an admin | `POST auth/register` | public |
| Log in, get a token | `POST auth/login` | public |
| Who am I | `GET auth/me` | any |

## Guards (Admin)
| Function | Method and path |
|---|---|
| Add a guard | `POST guards/create` |
| List my guards (optional `?status=Active\|Suspended`) | `GET guards/list` |
| Suspend or re-activate | `PATCH guards/set-status?guardId=` body `{ "status": "Suspended" }` |
| Remove a guard | `DELETE guards/delete?guardId=` |

## Properties
| Function | Method and path | Who |
|---|---|---|
| Add a property | `POST properties/create` | Admin |
| List properties I can see | `GET properties/list` | any |
| One property | `GET properties/get?propertyId=` | any |
| Edit a property | `PUT properties/update?propertyId=` | Admin |
| Delete a property | `DELETE properties/delete?propertyId=` | Admin |
| Guards of a property | `GET properties/list-guards?propertyId=` | Admin |
| Give a guard access | `PUT properties/assign-guard?propertyId=&guardId=` | Admin |
| Take access away | `DELETE properties/unassign-guard?propertyId=&guardId=` | Admin |

## Cameras
| Function | Method and path | Who |
|---|---|---|
| Add a camera | `POST cameras/create?propertyId=` | Admin |
| List cameras | `GET cameras/list?propertyId=` | any |
| One camera | `GET cameras/get?propertyId=&cameraId=` | any |
| Edit a camera | `PUT cameras/update?propertyId=&cameraId=` | Admin |
| Delete a camera (409 if a wall uses it) | `DELETE cameras/delete?propertyId=&cameraId=` | Admin |
| Can the server reach this host:port? (nothing saved) | `POST cameras/test-connection?propertyId=` | Admin |

Body: `name`, `sourceType` (`MobileApp`, `Onvif`, `Rtsp`), `host`, `port`, `streamPath`, optional `username`, `password`, `isActive`.
The response never contains the password (only `hasPassword`). `playbackUrl` is what the apps play.

## Laser grids
| Function | Method and path | Who |
|---|---|---|
| Add a grid | `POST laser-grids/create?propertyId=` | Admin |
| List grids | `GET laser-grids/list?propertyId=` | any |
| One grid | `GET laser-grids/get?propertyId=&gridId=` | any |
| Edit a grid (409 if it would drop a linked square) | `PUT laser-grids/update?propertyId=&gridId=` | Admin |
| Delete a grid (409 if a wall uses it) | `DELETE laser-grids/delete?propertyId=&gridId=` | Admin |

Body: `name`, `rows`, `columns`, `squareSizeMeters`.

## Walls (camera + wall annotation + cells + laser routes)
| Function | Method and path | Who |
|---|---|---|
| Add a wall (annotation required) | `POST walls/create?propertyId=` | Admin |
| List walls (with cells and routes) | `GET walls/list?propertyId=` | any |
| One wall | `GET walls/get?propertyId=&wallId=` | any |
| Edit a wall | `PUT walls/update?propertyId=&wallId=` | Admin |
| Delete a wall | `DELETE walls/delete?propertyId=&wallId=` | Admin |
| Save all cells of a wall | `PUT walls/save-cells?propertyId=&wallId=` | Admin |
| Set the laser route of one cell | `PUT walls/set-cell-route?propertyId=&wallId=&cellId=` | Admin |
| Is the wall ready? | `GET walls/validate?propertyId=&wallId=` | any |

Wall body: `name`, `cameraId`, `frameWidth`, `frameHeight`, `wallTopPoints` (2 points `[x,y]`), `wallBottomPoints` (2 points), optional `laserGridId`, `cameraGridRow`, `cameraGridCol`.
Cells body (a list): `label`, `lengthMeters`, `topLeft`, `bottomLeft`, `topRight`, `bottomRight` (each `[x,y]`).
Route body (a list, in drone visiting order): `[{ "row": 0, "col": 0 }, ...]`.

## Detection (talks to the ML service)
| Function | Method and path | Who |
|---|---|---|
| Start watching a wall | `POST detection/start?propertyId=&wallId=[&threshold=0.5]` | Admin |
| Send one JPEG frame (multipart: `frame`, optional `frameIndex`, `timestampSeconds`) | `POST detection/send-frame?propertyId=&wallId=` | Admin |
| Stop watching | `POST detection/stop?propertyId=&wallId=` | Admin |
| Stored alarms, newest first | `GET detection/list-events?propertyId=&wallId=&page=1&pageSize=20` | any |
| Is the ML service running? | `GET ml/health` | Admin |

Paged lists return `{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0 }`.
