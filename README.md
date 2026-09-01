# Dr. Care Backend

This is the .NET 10 modular-monolith backend for the Dr. Care internal operations platform.

## Structure

- `DrCare.Domain` contains business entities, enums, and lead state rules.
- `DrCare.Application` contains use cases, DTOs, authorization checks, and ports.
- `DrCare.Infrastructure` contains PostgreSQL persistence, private object storage, PDF rendering, and JWT/password adapters.
- `DrCare.Api` contains HTTP contracts, middleware, authentication, rate limiting, and health endpoints.

## Implemented API surface (v1)

- `POST /api/v1/auth/login`
- `GET /api/v1/auth/me`
- `GET /api/v1/leads`
- `POST /api/v1/leads`
- `GET /api/v1/leads/{leadId}`
- `POST /api/v1/leads/{leadId}/inquiry/start`
- `PATCH /api/v1/leads/{leadId}/inquiry`
- `POST /api/v1/leads/{leadId}/inquiry/submit`
- `GET /api/v1/leads/{leadId}/activities`
- `POST /api/v1/auth/refresh`, `/logout`, `/forgot-password`, `/reset-password`
- User administration, reference data, controlled lead search, inquiry, nurturing, qualification, assignment, activities, and tasks
- Location capture as inquiry data, finance/payment queue, invoice generation/confirmation, and private document intents/completion/download URLs
- Contract generation/review/approval/revision and secure two-party electronic signing links
- Product-specific pre-launch checklists and endorsement handoff/acknowledgement
- Operational queues, reports, notifications, audit logs, and `/api/health/live`/`/api/health/ready`

State-changing workflow operations use explicit action routes. There is intentionally no generic lead patch endpoint that can mass-assign state, approvals, ownership, or financial fields.

## Local run

For the complete macOS setup with Colima, email configuration, health checks, and troubleshooting, see [../docs/MAC_COLIMA_RUN_GUIDE.md](../docs/MAC_COLIMA_RUN_GUIDE.md).

1. Copy `.env.example` to `.env` and replace every placeholder.
2. Start PostgreSQL and the API with `docker compose up --build`.
3. Local development seeds the named test accounts below using `DEVELOPMENT_ADMIN_PASSWORD`.

| Role | Test accounts |
| --- | --- |
| Marketing Admin | `admin@drcare.local` (Maria Santos), `admin.2@drcare.local` (Sofia Mendoza) |
| Marketing Agent | `marketing.agent@drcare.local` (Juan Dela Cruz), `marketing.agent.2@drcare.local` (Daniel Cruz) |
| General Manager | `general.manager@drcare.local` (Carlos Reyes), `general.manager.2@drcare.local` (Beatrice Lim) |
| Finance | `finance@drcare.local` (Liza Bautista), `finance.2@drcare.local` (Rafael Santos) |
| Admin Team | `admin.team@drcare.local` (Paolo Navarro), `admin.team.2@drcare.local` (Nina Garcia) |
| Leadership | `leadership@drcare.local` (Ana Villanueva), `leadership.2@drcare.local` (Victor Aquino) |

All seeded accounts use the configured development password.

The compose file is for local development. Production should run with `ASPNETCORE_ENVIRONMENT=Production`, secrets injected by the deployment platform, private PostgreSQL access, TLS at the edge, and migrations applied as an explicit release step.

Development uses `Storage__Provider=Local`. Generated PDFs and browser uploads receive short-lived signed URLs just like the S3 flow, while file bytes are stored under `Storage:Local:RootPath` (or the Docker `local_storage` volume). A placeholder S3 bucket name does not override the explicit local provider. When AWS is ready, set `Storage__Provider=S3`, configure `Storage__S3__BucketName`, region, and AWS credentials. Production rejects the local provider and requires an S3 bucket.

Email is disabled in the tracked Development settings. To test Microsoft Graph locally, copy `src/DrCare.Api/appsettings.Development.local.example.json` to `appsettings.Development.local.json` and add the tenant, client, secret, and sender values. The local override is ignored by Git and loaded automatically in Development.

To apply the schema during a release, run `dotnet ef database update --project src/DrCare.Infrastructure --startup-project src/DrCare.Api` with the production connection string supplied through environment configuration.

## Security baseline

The API includes JWT issuer/audience/signature validation, short-lived access tokens, hashed rotating refresh tokens, single-use password-reset tokens, PBKDF2 password hashing, organization scoping, agent object-level authorization, DTO allowlists, capped pagination, request-size limits, rate limits, safe ProblemDetails responses, security headers, and append-only activity logs. Document objects use private signed URLs (local in Development and S3 in Production), ownership-bound keys, expiry checks, extension/content-type/size validation, and SHA-256 completion checks. Production should add malware scanning/quarantine before documents become business-authoritative.

PostgreSQL is the selected database for the initial build. Persistence is isolated behind application interfaces so a later SQL Server adapter remains possible if the organization requires it.
