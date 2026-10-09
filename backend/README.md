# Backend — ASP.NET Core 10 API

| Project | Role |
|---|---|
| `src/Inventory.Domain` | Entities and business rules (no framework dependencies). |
| `src/Inventory.Application` | Use cases, DTOs, validators, interfaces (`ICurrentUser`, `IWarehouseAccess`, `IAuditWriter`). |
| `src/Inventory.Infrastructure` | EF Core + PostgreSQL, ASP.NET Identity, JWT/refresh tokens, seeding, migrations. |
| `src/Inventory.Api` | Controllers under `/api/v1`, auth, permission policies, ProblemDetails, rate limiting, Swagger, health. |
| `tests/Inventory.Tests` | Unit tests. |
| `tests/Inventory.IntegrationTests` | Full API tests against a real PostgreSQL database. |

## Requirements

* .NET SDK 10.0.1xx (`global.json`)
* PostgreSQL 15+ (17 recommended), run in Docker: `docker compose -f deploy/docker-compose.dev.yml up -d` from the repo root.
* Visual Studio 2022 17.14+ (or 2026), or VS Code with C# Dev Kit. Test Explorer uses Microsoft.Testing.Platform (set in `global.json`).

## First run

In Visual Studio, set the secrets via right-click *Inventory.Api* → *Manage User Secrets*:

```json
{
  "Jwt:SigningKey": "<at least 32 random characters>",
  "Seed:AdminEmail": "you@yourcompany.mu",
  "Seed:AdminPassword": "<10+ chars, upper, lower, digit>"
}
```

Then start *Inventory.Api* with the **http** profile. Or from PowerShell in `backend/`:

```powershell
dotnet tool restore
dotnet user-secrets --project src/Inventory.Api set "Jwt:SigningKey" "<at least 32 random characters>"
dotnet user-secrets --project src/Inventory.Api set "Seed:AdminEmail" "you@yourcompany.mu"
dotnet user-secrets --project src/Inventory.Api set "Seed:AdminPassword" "<10+ chars, upper, lower, digit>"
dotnet run --project src/Inventory.Api --launch-profile http
```

* API: http://localhost:5080 — Swagger UI: http://localhost:5080/swagger — health: http://localhost:5080/health
* In Development the database is migrated and seeded on start-up (permissions, roles, the `MAIN` warehouse, and the first administrator if there are no users).
* Then run the web app (`cd ../frontend; npm start`). It proxies `/api` to port 5080.

Generate a signing key: `[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }) -as [byte[]])`

## Tests

```powershell
dotnet test                                   # unit + integration
$env:INVENTORY_TEST_DB = "Host=localhost;Port=5432;Username=postgres;Password=postgres"   # default
```

Integration tests create a database per test class (`inventory_test_<guid>`) and drop it afterwards, so they need a PostgreSQL login that may create databases.

## Migrations

```powershell
dotnet ef migrations add <Name> -p src/Inventory.Infrastructure -s src/Inventory.Infrastructure -o Persistence/Migrations
dotnet ef migrations script -p src/Inventory.Infrastructure -s src/Inventory.Infrastructure --idempotent -o migrate.sql
```

Production applies migrations from a reviewed SQL script (`Database:MigrateOnStartup` is false by default).

## Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection |
| `Jwt:SigningKey` | ≥ 32 chars, **secret** (user-secrets in dev, environment variable `Jwt__SigningKey` in production) |
| `Jwt:AccessTokenMinutes` / `RefreshTokenDays` | 15 / 14 by default |
| `Jwt:MaxFailedAttempts` / `LockoutMinutes` | 5 / 15 by default |
| `Seed:AdminEmail` / `Seed:AdminPassword` | First administrator, created only when no users exist |
| `RateLimiting:LoginPerMinute` / `RefreshPerMinute` | Per client IP |
| `Cors:AllowedOrigins` | Only needed if the web app is served from another origin |
| `ReverseProxy:KnownProxies` | IPs whose `X-Forwarded-*` headers are trusted |

## Security notes

* Every endpoint requires a signed-in user unless marked `[AllowAnonymous]`. Sensitive endpoints add `[HasPermission(...)]`.
* Permissions travel in the access token (valid ≤ 15 min). Warehouse access is read from the database on every request.
* Refresh tokens are stored as SHA-256 hashes, rotated on every use. Reuse of an old token (after a 20 s grace period) revokes the whole token family.
* `audit_logs` is append-only, enforced by a database trigger.
* To do before production (Phase 9): persist ASP.NET Data Protection keys (currently in-memory/user profile), HTTPS certificate and reverse proxy.
