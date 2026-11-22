# How AeroSentry works

> DRAFT written for review. Rewrite any sentence in your own words before the exam.

## Feature 1: Auth and roles

### What it does
Admins sign up themselves. An admin creates guard accounts. Everyone signs in
through one login endpoint and receives a JWT. The token carries the user's role,
and the server uses it to decide what each caller may do.

### The request flow
Client -> Controller -> Service -> Data (AppDbContext) -> SQL Server

1. **Controller** (`AuthController`, `GuardsController`) receives the HTTP request,
   ASP.NET validates the DTO, and the controller turns the service result into a
   status code. It never touches the database.
2. **Service** (`AuthService`, `GuardService`) holds the real logic: hash a password,
   verify it, create the token, check duplicates.
3. **Data** (`AppDbContext`, `User`) maps the `Users` table. EF Core turns LINQ into SQL.
4. **Database**: `Users` has a unique index on `Email`.

### Passwords
We never store a password. `PasswordHasher<User>` hashes it with PBKDF2 and a random
salt, and the salt is stored inside the hash string. The same password therefore
produces a different hash for each user. At login we verify the typed password
against the stored hash. Nothing is ever decrypted.

### The JWT
A token is `header.payload.signature`. The payload holds claims (`sub`, `email`,
`role`, `exp`, ...) and is readable by anyone. The signature is
`HMAC-SHA256(header.payload, Jwt:Key)`. If anyone edits the payload, the server
recomputes the HMAC with its own key, gets a different result, and rejects the token
with 401. Only the server knows the key, so only the server can create valid tokens.

### Validation and roles
- `AddJwtBearer` checks signature, issuer, audience and expiry on each request.
- `MapInboundClaims = false` keeps claim names exactly as we wrote them (`sub`, `role`).
- `RoleClaimType = "role"` tells ASP.NET which claim `[Authorize(Roles = ...)]` reads.
- `OnTokenValidated` also checks the database: a suspended or deleted user gets 401
  even with an unexpired token. This gives up some of JWT's statelessness so that
  suspension takes effect immediately.
- `UseAuthentication()` must come before `UseAuthorization()` in `Program.cs`.

### Status codes
- 400: the DTO failed validation.
- 401: we don't know who you are (no token, bad token, expired, user suspended).
- 403: we know who you are but your role is not allowed (guard on an Admin endpoint),
  or a suspended guard tries to log in.
- 409: email already registered.

### Why decisions were made
- The role is never accepted from the client. The server sets it (public register = Admin,
  `POST guards/create` = Guard), so nobody can promote themselves.
- Login returns the same 401 for a wrong email and a wrong password, so attackers
  cannot discover which emails exist.
- Login is a POST so the password travels in the body, not in a URL that gets logged.
- Services are Scoped because they depend on the Scoped `AppDbContext`.
- DTOs stop overposting (a client sending `role`) and keep `PasswordHash` out of responses.

### Guard management (Admin only)
- `GET guards/list?status=` builds the query with `Where` on an `IQueryable`, so SQL Server does the
  filtering. Only `ToListAsync()` runs it.
- `PATCH guards/set-status?guardId=` flips `IsActive`. The `Role == "Guard"` filter means an Admin can
  never be suspended or deleted through these endpoints.
- `DELETE guards/delete?guardId=` returns 204 with no body. A deleted user's old token stops working
  because of the `OnTokenValidated` database check.

### Not done yet
- Password reset by email (needs a mail server, so it does not fit the offline rule).
- Rate limiting on login.
- Pagination on the guards list.

---

## Feature 2: Properties

### What it does
An admin owns properties (a house, a yard, a site). The admin adds, edits and deletes them,
and gives guards access to them. A guard only sees the properties they were given access
to, and can only read them.

### The map: which file does what, in the order a request travels
| Order | File | Its one job |
|---|---|---|
| 1 | `Program.cs` | Registers `PropertyService` (`AddScoped`), switches on authentication |
| 2 | `Controllers/PropertiesController.cs` | Reads the token, calls the service, turns the result into a status code. No logic |
| 3 | `DTOs/PropertyRequest.cs` | What the client may send (name, address, optional coordinates) and its validation |
| 4 | `Services/PropertyService.cs` | All the logic: who may see what, create, update, assign a guard |
| 5 | `Data/AppDbContext.cs` | Maps the tables and their rules (lengths, foreign keys, what happens on delete) |
| 6 | `Data/Entities/Property.cs`, `PropertyGuard.cs` | The two tables as C# classes |
| 7 | `DTOs/PropertyResponse.cs` | What we send back (never the entity itself) |
| 8 | `Migrations/..._AddPropertiesAndGuardOwnership.cs` | The SQL that created the tables |

Flow: Client -> `PropertiesController` -> `PropertyService` -> `AppDbContext` -> SQL Server.

### The functions
| Function | Who | Success | Errors |
|---|---|---|---|
| `POST properties/create` | Admin | 201 | 400 (bad body, or only one of latitude/longitude) |
| `GET properties/list` | any | 200 | |
| `GET properties/get?propertyId=` | any | 200 | 404 |
| `PUT properties/update?propertyId=` | Admin | 200 | 400, 404 |
| `DELETE properties/delete?propertyId=` | Admin | 204 | 404 |
| `GET properties/list-guards?propertyId=` | Admin | 200 | 404 |
| `PUT properties/assign-guard?propertyId=&guardId=` | Admin | 204 | 404 |
| `DELETE properties/unassign-guard?propertyId=&guardId=` | Admin | 204 | 404 |
401 everywhere without a valid token; 403 when a guard calls an Admin function.

### The central idea: ownership
The caller's id and role come from the **token** (`sub` and `role` claims), never from the request.
`PropertiesController` reads them in `CallerId` and `CallerRole`. The service then asks one question in one
place, `VisibleTo(userId, role)`:
- Admin: properties where `OwnerAdminId` equals my id.
- Anyone else (a guard): properties that have a `PropertyGuards` row with my id.
Every read goes through it, so an admin can never read another admin's property, whatever id they guess.
Writes use `OwnerAdminId == adminId` in the same way.

### "Not yours" looks the same as "does not exist"
When the property is not visible to the caller, the service returns `null` (or `false`) and the controller
answers **404**, not 403. A 403 would tell an attacker "this id exists, it just isn't yours". With 404 they
cannot find out which ids are real.

### The two tables
- `Properties`: `Id`, `OwnerAdminId`, `Name` (max 200), `Address` (max 300), optional `Latitude` and
  `Longitude`, `CreatedAtUtc`. `OwnerAdminId` is a foreign key to `Users` with **Restrict**: deleting an admin
  never silently deletes their properties.
- `PropertyGuards` is a **join table**: one row means "this guard works at this property". A guard can work at
  many properties and a property can have many guards. Its primary key is the pair (`PropertyId`, `GuardId`),
  so the same guard can never be assigned twice. Both foreign keys are **Cascade**: delete the property or the
  guard and their assignment rows go too.
- `Users.OwnerAdminId` (added by the same migration) says which admin created a guard. An admin can only assign
  *their own* guards (`Role == "Guard" && OwnerAdminId == adminId`).

### Details worth knowing
- Latitude and longitude must come together or not at all. The controller checks this and returns 400.
  The DTO also limits them: latitude -90 to 90, longitude -180 to 180.
- `Name` and `Address` are trimmed before saving.
- `list` builds the query on an `IQueryable` (`VisibleTo(...).OrderBy(...)`), and only `ToListAsync()` sends one
  SQL query. The filtering happens in SQL Server, not in memory.
- Assigning the same guard twice is harmless (the service checks first and returns 204 either way).
- `list-guards` reuses `GuardService.ToResponse`, so a guard looks the same everywhere.
- From Feature 3 on, deleting a property also deletes everything that hangs under it (cameras, walls, ...).

### Why decisions were made
- Controllers hold no logic, so the rules live in one testable place (the service).
- The 404-for-not-yours rule and the single `VisibleTo` method mean there is exactly one place to get access control
  wrong, and it is easy to read.
- A join table (not a list inside `Property`) is how a relational database stores many-to-many.
- DTOs stop the client from sending `OwnerAdminId` and keep internal fields out of responses.

### How to check it yourself (Scalar at `/scalar`, or Postman)
1. Register an admin (`POST auth/register`), log in, copy the token (`Authorize` button in Scalar).
2. `POST properties/create` with `{ "name": "Home", "address": "Street 1" }`: expect 201 and an `id`.
3. `GET properties/list`: your property is there. `GET properties/get?propertyId=<id>`: 200.
4. Register a second admin and log in as them: `GET properties/get?propertyId=<the first admin's id>` gives **404**.
5. Create a guard (`POST guards/create`), assign it (`PUT properties/assign-guard`), log in as the guard:
   `properties/list` shows the property, `POST properties/create` as the guard gives **403**.
6. Ask SQL Server: `SELECT * FROM Properties; SELECT * FROM PropertyGuards;`

### Not done yet
- Pagination on the properties list.
- Transferring a property to another admin.
