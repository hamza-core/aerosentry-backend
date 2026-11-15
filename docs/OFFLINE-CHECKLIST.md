# Offline checklist

Do these once while you have internet. After that, everything runs with no internet.

## One-time downloads
- .NET SDK 11.0.100 (`dotnet --version`)
- Docker image: `mcr.microsoft.com/mssql/server:2022-latest`
  (`docker pull --platform linux/amd64 mcr.microsoft.com/mssql/server:2022-latest` on Apple Silicon)
- Global tool: `dotnet tool install --global dotnet-ef`
  (make sure `~/.dotnet/tools` is on PATH)
- NuGet packages restored into `~/.nuget/packages` by `dotnet restore`

## NuGet packages (AeroSentry.Api)
| Package | Why |
|---|---|
| Microsoft.AspNetCore.OpenApi | Generates the OpenAPI JSON |
| Scalar.AspNetCore | Browser API page, assets bundled (tested offline) |
| Microsoft.EntityFrameworkCore.SqlServer | EF Core provider for SQL Server |
| Microsoft.EntityFrameworkCore.Design | Needed by `dotnet ef` to create migrations |
| Microsoft.AspNetCore.Authentication.JwtBearer | Creates and validates JWTs |

`PasswordHasher<User>` needs no package. It is in the ASP.NET Core shared framework.

## Config keys
Secrets, set with `dotnet user-secrets set` inside `AeroSentry.Api` (they are NOT in git,
so redo them on every new machine):
- `ConnectionStrings:DefaultConnection`
- `Jwt:Key` (generate with `openssl rand -base64 48`)

Alternative on a machine without user-secrets: environment variables with a double
underscore, e.g. `ConnectionStrings__DefaultConnection`, `Jwt__Key`.

Non-secret, in `appsettings.json`: `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes`.

## Run everything from scratch
1. Start Docker, then create the database container (first time only):
   ```
   docker run --platform linux/amd64 -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<password>" \
     -p 1433:1433 -v aerosentry-sqldata:/var/opt/mssql --name aerosentry-sql \
     -d mcr.microsoft.com/mssql/server:2022-latest
   ```
   Next times: `docker start aerosentry-sql`.
2. Set the two secrets (above).
3. `cd AeroSentry.Api`
4. `dotnet ef database update` creates the `AeroSentry` database and applies migrations.
5. `dotnet run` starts the API. Open `/scalar` or use Postman.

## Commands and what each does
| Command | What it does |
|---|---|
| `dotnet restore` | Downloads/links NuGet packages |
| `dotnet build` | Compiles the project |
| `dotnet run` | Starts the API |
| `dotnet ef migrations add <Name>` | Generates a migration from model changes (database NOT changed) |
| `dotnet ef database update` | Applies pending migrations to the database |
| `dotnet ef migrations remove` | Deletes the last migration (reverts it first if applied) |
| `dotnet user-secrets set "<Key>" "<Value>"` | Stores a secret outside git |
| `dotnet user-secrets list` | Shows stored secrets |
| `docker ps -a` | Lists containers (running and stopped) |
| `docker start/stop aerosentry-sql` | Starts/stops the database container |

## Migrations applied so far
- `initialUserMigration`: creates `Users` (unique index on `Email`).
- `PHonenumberAdditionCheckMigration`: adds the optional `PhoneNumber` column to `Users`.
