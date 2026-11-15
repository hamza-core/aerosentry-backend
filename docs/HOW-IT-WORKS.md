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
  `POST /guards` = Guard), so nobody can promote themselves.
- Login returns the same 401 for a wrong email and a wrong password, so attackers
  cannot discover which emails exist.
- Login is a POST so the password travels in the body, not in a URL that gets logged.
- Services are Scoped because they depend on the Scoped `AppDbContext`.
- DTOs stop overposting (a client sending `role`) and keep `PasswordHash` out of responses.

### Not done yet
- Password reset by email (needs a mail server, so it does not fit the offline rule).
- Rate limiting on login.
- Pagination on the guards list.
