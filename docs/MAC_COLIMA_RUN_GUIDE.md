# Dr. Care local run guide for macOS + Colima

This guide runs PostgreSQL, the ASP.NET Core API, the durable email worker, and private local document storage in Docker. The Vite frontend runs on the Mac host.

The development Compose profile uses local storage instead of S3. Uploads, invoices, and generated contracts are kept in a named Docker volume and use the same signed upload/download flow. Production should use S3 and a secret manager.

## Prerequisites

```bash
brew install colima docker docker-compose
colima version
docker --version
docker compose version
```

## Start Colima

Apple Silicon on current macOS:

```bash
colima start --cpu 4 --memory 8 --disk 60 --vm-type=vz --mount-type=virtiofs
docker context use colima
docker info
docker context show
```

If `--vm-type=vz` is unavailable (for example on an older Intel Mac), omit that option:

```bash
colima start --cpu 4 --memory 8 --disk 60
```

If Docker Desktop is installed too, keep the Docker context set to `colima` for this run.

## Prepare backend configuration

From the repository root:

```bash
cd dr-care-backend
cp .env.example .env
```

Edit `.env` and replace the required local values:

```dotenv
POSTGRES_PASSWORD=use-a-long-random-local-password
JWT_SIGNING_KEY=use-at-least-32-random-characters
DEVELOPMENT_ADMIN_PASSWORD=use-a-strong-local-admin-password
STORAGE__PROVIDER=Local
LOCAL_STORAGE_SECRET=use-a-long-random-storage-secret
```

Do not commit or share `.env`. Host ports are API `127.0.0.1:8080` and PostgreSQL `127.0.0.1:5434`; the API reaches PostgreSQL inside Compose as `postgres:5432`.

## Configure Microsoft Graph email

The tracked `appsettings.Development.json` keeps email disabled and contains no credentials. Create the ignored local override before building:

```bash
cp src/DrCare.Api/appsettings.Development.local.example.json src/DrCare.Api/appsettings.Development.local.json
```

Edit `src/DrCare.Api/appsettings.Development.local.json`, set `Email:Enabled` to `true`, and provide `TenantId`, `ClientId`, `ClientSecret`, `FromAddress`, `FromName`, and `FrontendBaseUrl` (`http://localhost:5173`). The API loads this optional override after the tracked Development settings. The override is copied into the local publish output and Docker image when present, but is ignored by Git.

The worker uses a PostgreSQL outbox with retries, crash recovery, and attachment references, then sends through Microsoft Graph. No SES or separate queue service is required. The Entra application needs Microsoft Graph **Mail.Send** application permission with admin consent, and the sender mailbox must be valid.

Never commit or publish these credentials. They are development-only; production should inject them through a secret provider. To run without delivery, omit the local override or set `Email:Enabled` to `false`; queued messages remain pending.

## Validate and start the backend

```bash
docker compose config --quiet
docker compose up -d --build
docker compose ps
docker compose logs -f api
```

The API waits for a healthy PostgreSQL container and runs Development migrations automatically. The image includes Chromium for contract/invoice generation and prepares the non-root upload directory.

## Verify API and email

```bash
curl -fsS http://localhost:8080/api/health/live
curl -fsS http://localhost:8080/api/health/ready
docker compose logs -f api | grep -i -E 'email|outbox|graph'
```

Trigger a welcome, invoice, signing invitation, or password reset email in the UI. The API first writes the durable outbox row; the worker then marks it Sent or records a retry/failure. Microsoft Graph success is normally logged as `202 Accepted`.

Optional outbox inspection:

```bash
docker compose exec postgres psql -U drcare -d drcare -c \
  'select "Status", count(*) from "EmailOutboxMessages" group by "Status" order by "Status";'
```

## Start the frontend

In a second terminal:

```bash
cd ../dr-care-frontend
npm install
npm run dev -- --host 0.0.0.0
```

Open [http://localhost:5173](http://localhost:5173). The frontend development API target is `http://localhost:8080`.

## Stop and restart

```bash
cd ../dr-care-backend
docker compose down
```

`docker compose down` keeps named volumes. Rebuild after changing backend source, the Dockerfile, or `appsettings.Development.json`:

```bash
docker compose up -d --build
```

Start the existing stack later with `docker compose up -d`. Stop the Colima VM when you are finished:

```bash
colima stop
```

This permanently deletes the local database and uploaded files, so use it only for an intentional reset:

```bash
docker compose down -v
```

## Troubleshooting

Port conflicts:

```bash
lsof -nP -iTCP:8080 -sTCP:LISTEN
lsof -nP -iTCP:5434 -sTCP:LISTEN
```

API startup errors:

```bash
docker compose logs --tail=200 api
```

Common causes are missing `.env` values, an invalid JWT key, or enabled email validation with incomplete Graph credentials. PostgreSQL diagnostics:

```bash
docker compose logs --tail=200 postgres
docker compose exec postgres pg_isready -U drcare -d drcare
```

For upload/download failures, confirm `Storage__Provider=Local`, inspect API logs, and keep the `local_storage` volume. For pending email, verify Mail.Send permission/admin consent, sender mailbox, and Graph tenant/client values.

## Local endpoints

| Component | Address |
|---|---|
| Frontend | `http://localhost:5173` |
| API | `http://localhost:8080` |
| API live health | `http://localhost:8080/api/health/live` |
| API ready health | `http://localhost:8080/api/health/ready` |
| PostgreSQL (host access) | `localhost:5434` |


