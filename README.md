# Global Rubber – Machine & Mold Maintenance (MMM)

Backend for the Global Rubber Machine & Mold Maintenance system: ASP.NET Core 8 Web API on
Clean Architecture, backed by SQL Server via EF Core. The frontend (React/TypeScript template)
lives in [`../global-rubber-maintenance/`](../global-rubber-maintenance) as a separate project.

## Current Phase

```text
Current Phase:     Backend Foundation (Phase 1 of 10)
Business modules:  Not implemented yet
```

See [`../docs/GlobalRubber_MMM_System_Analysis.md`](../docs/GlobalRubber_MMM_System_Analysis.md)
for the full functional, database and API analysis that later phases will implement against.

```text
PHASE 1  Backend Foundation                 ← you are here
PHASE 2  Database Architecture & ER Design
PHASE 3  Security / Authentication / Authorization
PHASE 4  Master Modules
PHASE 5  Transaction Modules
PHASE 6  Dashboard APIs
PHASE 7  Reports APIs
PHASE 8  React Frontend integration
PHASE 9  Integration Testing
PHASE 10 Production Hardening
```

## Technology Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 8 (LTS), pinned via [`global.json`](global.json) |
| Web framework | ASP.NET Core Web API, controller-based |
| ORM | Entity Framework Core 8 (SQL Server provider) |
| Database | SQL Server |
| API docs | Swagger / OpenAPI (Swashbuckle.AspNetCore) |
| Health checks | Microsoft.Extensions.Diagnostics.HealthChecks + AspNetCore.HealthChecks.SqlServer |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / Api) |
| Testing | xUnit, Microsoft.AspNetCore.Mvc.Testing |

No third-party mediator, mapping or logging framework has been added yet - only what this phase
actually needs. Nullable reference types and implicit usings are enabled on every project.

## Solution Structure

Every project sits directly at the solution root (no `src`/`tests` wrapper folders), matching the
conventions used across this team's other .NET solutions.

```text
GlobalRubber.MMM.sln
global.json                           # pins the SDK to .NET 8

GlobalRubber.MMM.Domain/              # Constants/ Entities/  (folders only in this phase)
GlobalRubber.MMM.Application/
├── Common/                            ApiResponse.cs, PagedResult.cs, PaginationRequest.cs,
│                                       PaginationDefaults.cs, ValidationException.cs,
│                                       NotFoundException.cs, ForbiddenAccessException.cs
├── DTOs/                              (empty - per-module DTOs land here from Phase 4 onward)
├── Interfaces/                        IDateTimeProvider.cs
│   └── Repositories/                  (empty - repository interfaces land here)
├── Services/                          (empty - business service implementations)
├── Validators/                        (empty - FluentValidation validators)
├── AssemblyMarker.cs                  assembly-scanning marker
└── DependencyInjection.cs             AddApplication()

GlobalRubber.MMM.Infrastructure/
├── Data/
│   ├── GlobalRubberDbContext.cs
│   ├── Configurations/                (empty - IEntityTypeConfiguration<T> classes)
│   └── Migrations/                    (empty - no migrations created in this phase)
├── Repositories/                      (empty)
├── Services/                          DateTimeProvider.cs
└── DependencyInjection.cs             AddInfrastructure(configuration)

GlobalRubber.MMM.Api/
├── Controllers/                       BaseApiController.cs, SystemController.cs
├── Middlewares/                       GlobalExceptionHandler.cs
├── Extensions/                        ServiceCollectionExtensions.cs, WebApplicationExtensions.cs
├── Configuration/                     CorsOptions.cs, SwaggerOptions.cs, ApplicationOptions.cs
├── HealthChecks/                      SelfHealthCheck.cs, HealthCheckResponseWriter.cs
├── Filters/                           (empty)
├── Services/                          (empty - API-layer-only helper services)
├── Properties/launchSettings.json
├── Program.cs
├── appsettings.json
└── appsettings.Development.json

GlobalRubber.MMM.Tests/                single test project (unit + integration together)
├── ApiResponseTests.cs
├── PaginationTests.cs
├── ApiWebApplicationFactory.cs
├── HealthEndpointTests.cs
└── SystemControllerTests.cs
```

### Project references (Clean Architecture dependency rule)

```text
Api ──▶ Application ──▶ Domain
Api ──▶ Infrastructure ──▶ Application ──▶ Domain
Infrastructure ──▶ Domain            (explicit, in addition to the transitive path above)
Tests ──▶ Api, Application, Domain
```

`Domain` references nothing. `Application` references only `Domain` and a small DI-abstraction
package (no EF Core, no ASP.NET Core). `Infrastructure` implements the `Application` layer's
interfaces using EF Core and SQL Server. `Api` wires `Application` and `Infrastructure` together
and owns everything HTTP-specific.

## How to Run

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (this folder's
`global.json` selects it automatically if you have multiple SDKs installed).

```bash
dotnet restore
dotnet build
dotnet run --project GlobalRubber.MMM.Api
```

The API listens on the URLs in
[`GlobalRubber.MMM.Api/Properties/launchSettings.json`](GlobalRubber.MMM.Api/Properties/launchSettings.json)
(`http://localhost:5067` / `https://localhost:7279` by default) and opens Swagger automatically
in Development.

## Configuration

All configuration is read from `appsettings.json` (safe defaults / empty secrets),
`appsettings.Development.json` (local dev overrides) and, for anything sensitive, environment
variables or [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) -
**never hard-coded and never committed with real credentials.**

| Section | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string. Empty in `appsettings.json`; set per environment. |
| `Cors:AllowedOrigins` | Explicit allow-list of origins permitted to call the API (e.g. the React dev server). Never combined with "allow any origin". |
| `Swagger:*` | Title/version/description shown in the Swagger document. |
| `Application:*` | Display name and current phase, surfaced in logs and available to any future status endpoint. |

To point at a local SQL Server / LocalDB instance for development, edit
`ConnectionStrings:DefaultConnection` in `appsettings.Development.json`, or override it with a
user secret:

```bash
dotnet user-secrets init --project GlobalRubber.MMM.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=...;..." --project GlobalRubber.MMM.Api
```

## Database Configuration

No business tables, entities or migrations exist yet - this phase only registers an empty
`GlobalRubberDbContext` (`GlobalRubber.MMM.Infrastructure/Data`) so the connection can be
exercised by the `/readiness` and `/health` endpoints. Once the database design phase is
approved, entities go under `GlobalRubber.MMM.Domain/Entities`, their EF Core configurations
under `GlobalRubber.MMM.Infrastructure/Data/Configurations`, and migrations are added with:

```bash
dotnet ef migrations add <Name> \
  --project GlobalRubber.MMM.Infrastructure \
  --startup-project GlobalRubber.MMM.Api
```

## Swagger

Available in the Development environment only:

- `GET /swagger` - Swagger UI
- `GET /swagger/v1/swagger.json` - the OpenAPI document

## Health Checks

| Endpoint | Checks | Purpose |
|---|---|---|
| `GET /liveness` | `self` (tag `live`) | Is the process alive? Never touches the database. |
| `GET /readiness` | `database` (tag `ready`) | Is the API ready to serve requests, including DB connectivity? |
| `GET /health` | every registered check | Combined status for monitoring/dashboards. |

All three return JSON (not the framework default plain text), for example:

```json
{
  "status": "Healthy",
  "totalDurationMs": 12.4,
  "checks": {
    "database": { "status": "Healthy", "description": null, "durationMs": 11.9 }
  }
}
```

`200 OK` when every included check is `Healthy`; `503 Service Unavailable` otherwise - which is
the *correct* response when, for example, no SQL Server is reachable, not a bug. The database
check times out after 3 seconds so `/health` and `/readiness` stay fast even when SQL Server is
completely unreachable.

## Testing

```bash
dotnet test
```

A single `GlobalRubber.MMM.Tests` project holds both kinds of test:

- **Foundation unit tests** - `ApiResponse`/`ApiResponse<T>` and the `PagedResult<T>`/
  `PaginationRequest` pagination foundation. No business logic exists yet, so there is nothing
  business-specific to unit test.
- **Integration tests** - boot the real `Api` project in-memory
  (`Microsoft.AspNetCore.Mvc.Testing`, see `ApiWebApplicationFactory`) and assert `/liveness`,
  `/readiness`, `/health`, `/api/v1/system/ping` and Swagger all respond correctly - including
  tolerating an unreachable database, since `/readiness` and `/health` are expected to report
  `Unhealthy`/503 in that case, not fail the test.

## Development Rules

- **Clean Architecture dependency direction is enforced by project references** - `Domain` has
  zero dependencies; `Application` depends only on `Domain`; `Infrastructure` and `Api` are the
  only projects allowed to reference EF Core / ASP.NET Core / SQL Server directly.
- **No controller contains business logic, SQL, or a `try`/`catch`.** All exceptions flow to
  `GlobalExceptionHandler` (`GlobalRubber.MMM.Api/Middlewares`), which maps them to the standard
  `ApiResponse` envelope and the right HTTP status code.
- **Every response is `ApiResponse` or `ApiResponse<T>`**
  (`GlobalRubber.MMM.Application/Common/ApiResponse.cs`), so the React frontend always parses the
  same shape.
- **Every list endpoint returns `PagedResult<T>`**
  (`GlobalRubber.MMM.Application/Common/PagedResult.cs`) - page number/size, total count/pages,
  has-next/has-previous.
- **Every async operation accepts a `CancellationToken`** and passes it all the way down to
  `SaveChangesAsync(cancellationToken)` / EF Core queries, once those exist.
- **No hard-coded configuration or magic strings** where an `IOptions<T>` class or a named
  constant belongs (see `GlobalRubber.MMM.Api/Configuration`).
- **No secrets are ever committed** - see `.gitignore` and the Configuration section above.

## What Is *Not* Yet Implemented

Per the Phase 1 scope, none of the following exist yet - they are built in later phases once the
corresponding design is approved:

- Business entities (Machine, Mold, Product, Department, Employee, Vendor, SparePart,
  MaintenanceType, BreakdownType, Checklist, ProductionEntry, MachineMaintenance,
  MoldMaintenance, MachineBreakdown, WorkOrder, SparePartUsage, ...)
- Business controllers, services, repositories or DTOs for any of the above
- Database migrations or business database tables
- Authentication / authorization (JWT, roles, permissions)
- Dashboard, report or search endpoints
