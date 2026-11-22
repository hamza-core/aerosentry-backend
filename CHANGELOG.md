# Changelog

## Unreleased

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
