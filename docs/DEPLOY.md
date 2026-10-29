# Lindrake — Deploy Guide

Stack: ASP.NET Core Web API (`api`) + Blazor WASM served by nginx (`web`). Data: Postgres with pgvector and pg_trgm, in **qavren-db** (the shared Supabase Postgres, schema `lindrake`) for deployments and in the compose `db` container for local development. See [Database (qavren-db)](#database-qavren-db).

---

## Routing overview

| Path | Serves |
|------|--------|
| `/` | Marketing site (`landing/`) — static HTML/CSS/JS |
| `/app/` | Blazor WebAssembly SPA (nginx rewrites to `/app/index.html`) |
| `/app/appsettings.json` | Client runtime config, rendered from `API_BASE_URL` at container start, never cached |
| `http://api:8080` | ASP.NET Core API (host port 5000; on `127.0.0.1` only with the prod overlay, see [Ports](#ports)) |

The nginx config (`src/Lindrake.Web/nginx.conf`) routes `/app/` to `/usr/share/nginx/html/app/` and falls back to `index.html` for SPA deep-links. `landing/` is copied to the nginx root so `/` serves the marketing site. The web container does **not** proxy `/api`.

How the browser reaches the API is decided by `API_BASE_URL` (web container):

- **Same origin (recommended):** `API_BASE_URL=` (empty). The client calls `api/...` on the origin that served it, so the reverse proxy in front must route `/api/` (and `/health` if you want it public) to `api:8080` and everything else to `web:80`. No CORS needed.
- **Separate origin:** `API_BASE_URL=https://api.example.com`, and the API must allow the web origin: `Cors__AllowedOrigins__0=https://app.example.com` (no origins are allowed outside Development by default).

The entrypoint script rejects any other value (it must be empty or an absolute `http(s)` URL), and the container then fails to start.

---

## GHCR image names

The deploy workflow pushes to GitHub Container Registry under the repo owner's namespace:

| Service | Image |
|---------|-------|
| Web (nginx + Blazor WASM) | `ghcr.io/stevenfackley/lindrake-web` |
| API (ASP.NET Core) | `ghcr.io/stevenfackley/lindrake-api` |

Each run tags images with:
- `latest` — always the most recent build
- `prod-<7-char-SHA>` — immutable, e.g. `prod-3810ff9`

---

## CI/CD workflow

`.github/workflows/deploy.yml` runs **only on `workflow_dispatch`** (Actions → deploy → Run workflow; the `environment` input offers `production`). There is no push trigger: it was removed until infrastructure is provisioned.

### Job 1 — `build-push` (no live host needed)

1. Logs in to GHCR with the built-in `GITHUB_TOKEN` (`packages: write` permission).
2. Builds `src/Lindrake.Web/Dockerfile` (build context: repo root) → pushes as `lindrake-web`.
3. Builds `src/Lindrake.Api/Dockerfile` (build context: repo root) → pushes as `lindrake-api`.
4. Uses GitHub Actions cache (`type=gha`) per image to speed up layer reuse.

### Job 2 — `deploy` (no-op until secrets are set)

A first step checks `DEPLOY_HOST`; when it is empty the job logs that infra is not provisioned and skips the SSH step. When `DEPLOY_HOST` is set but `LINDRAKE_DB_URL_PROD` is not, the job fails. Otherwise it SSHes to the production host, writes `secrets/ConnectionStrings__DefaultConnection.txt` from `LINDRAKE_DB_URL_PROD` (mode 600), and runs:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml pull web api
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --no-deps --remove-orphans web api
sleep 30 && docker compose ps
```

The final `docker compose ps` is informational; the job does not wait for the containers to report healthy.

---

## Required GitHub secrets

Set these in **Settings → Secrets and variables → Actions** on the GitHub repo.

### Always required (GHCR push)

None beyond the built-in `GITHUB_TOKEN` — it has `packages: write` automatically.

### Required for the SSH deploy step

| Secret | Description |
|--------|-------------|
| `DEPLOY_HOST` | SSH hostname or IP of the production server |
| `DEPLOY_USER` | SSH username (must be in the `docker` group) |
| `DEPLOY_SSH_KEY` | PEM-encoded private SSH key (passphrase-free) |
| `DEPLOY_WORK_DIR` | Absolute path on host where compose files live, e.g. `/srv/lindrake` |
| `LINDRAKE_DB_URL_PROD` | Npgsql connection string to qavren-db prod as role `lindrake`, over the session pooler: `Host=<pooler host>;Port=5432;Database=postgres;Username=lindrake.<project ref>;Password=<password>;SSL Mode=Require`. Written to the API's connection-string secret file on every deploy |

Until `DEPLOY_HOST` is set, the `deploy` job's SSH step is skipped; `build-push` still runs and images are pushed to GHCR.

A test environment would use `LINDRAKE_DB_URL_TEST` (qavren-db test, `aws-1-us-east-1.pooler.supabase.com`) the same way; the workflow offers only `production` today.

---

## Prerequisites on the production host

Before the first SSH deploy, the following must exist on the server at `$DEPLOY_WORK_DIR`:

```
docker-compose.yml         # copied from repo
docker-compose.prod.yml    # copied from repo
.env.prod                  # production settings; do NOT commit real values
secrets/
  Anthropic__ApiKey.txt
  OpenAI__ApiKey.txt
  Tavily__ApiKey.txt
  Jwt__Secret.txt
  ConnectionStrings__DefaultConnection.txt   # written by deploy.yml (LINDRAKE_DB_URL_PROD)
```

### .env.prod

`docker-compose.prod.yml` removes the `db` service (`db: !reset null`) and the api's dependency on it, replaces each service's `env_file` with `.env.prod` alone (`env_file: !override`) and replaces the `api` `environment` block (which sets `Database__Schema=lindrake`), so nothing from `.env.dev` or the base file's dev mappings reaches production; `.env.dev` is not needed on the host. The `!override` / `!reset` tags need **Docker Compose 2.24.4 or later**; older versions do not apply them. Check the merged result with `docker compose -f docker-compose.yml -f docker-compose.prod.yml config`.

`.env.prod` is passed to every service. Set at least:

| Key in `.env.prod` | Why |
|---|---|
| `Supabase__Url=https://<project>.supabase.co` | Production auth. The Supabase project must emit a `tenant_id` custom claim. |
| *or* `JWT__ALLOWLOCALIDENTITYPROVIDER=true` | Run the in-house HS256 provider deliberately instead. Requires a unique `Jwt__Secret` of at least 32 bytes (`secrets/Jwt__Secret.txt`); the public development default is rejected. With neither key the API refuses to start in Production. `Jwt__Issuer` / `Jwt__Audience` default to `Lindrake` (`appsettings.json`); set them here to change that. |
| `API_BASE_URL=https://api.example.com` | Only when the API is on a different origin. Unset or empty = same origin, the recommended setup. |
| `Cors__AllowedOrigins__0=...` | Only when `API_BASE_URL` is a different origin from the web app. |

No `POSTGRES_*` setting belongs in `.env.prod`: there is no database container in production.

Compose `${...}` interpolation (`IMAGE_TAG`, `IMAGE_API`, `IMAGE_WEB`) reads the shell environment or a `.env` file next to the compose files, never `env_file`. `deploy.yml` passes no `--env-file`, so put such values in `$DEPLOY_WORK_DIR/.env` if you need them.

### Populating secrets on first deploy

```bash
# Example — source from environment or your secrets manager
echo -n "$ANTHROPIC_KEY"       > secrets/Anthropic__ApiKey.txt
echo -n "$OPENAI_KEY"          > secrets/OpenAI__ApiKey.txt    # may be empty with ONNX embeddings
echo -n "$TAVILY_KEY"          > secrets/Tavily__ApiKey.txt
echo -n "$(openssl rand -hex 32)" > secrets/Jwt__Secret.txt     # used only by the HS256 provider
chmod 600 secrets/*.txt
```

`secrets/ConnectionStrings__DefaultConnection.txt` is written by `deploy.yml` from `LINDRAKE_DB_URL_PROD`; for a manual deploy write it the same way (`printf '%s' "$LINDRAKE_DB_URL" > ...`).

`secrets/*.txt` is gitignored. In automated pipelines, write these files from Vault, AWS Secrets Manager, Azure Key Vault, etc., at deploy time before running compose. Create every file the overlay lists, even one whose value is unused in your auth/embedding mode.

### Database (qavren-db)

Deployments keep no database of their own. Lindrake's data lives in **qavren-db**, the shared Supabase Postgres (`stevenfackley/qavren-db`): one schema and one login role per app, Postgres grants as the wall between apps, extensions enabled project-wide in the `extensions` schema. Lindrake's are schema `lindrake` and role `lindrake`.

1. **Provision** (once per environment, from the qavren-db repo): `pwsh tools/provision-app.ps1 -App lindrake -Env test -Apply`, then `-Env prod`. It creates the role (no superuser, no BYPASSRLS) and the schema it owns, sets the role's `search_path` to `lindrake, extensions` and grants USAGE on `extensions`. It prints the password once: store it straight away as the `LINDRAKE_DB_URL_PROD` (or `LINDRAKE_DB_URL_TEST`) GitHub secret.
2. **Extensions:** the migrations need `vector`, `pg_trgm` and `uuid-ossp`, and the role cannot create extensions. They must already be enabled in the project's `extensions` schema (and listed in qavren-db's `apps/lindrake.yaml` so the restore drill recreates them); the migrations' `CREATE EXTENSION IF NOT EXISTS` then no-ops.
3. **Connection string:** the Supavisor **session** pooler, port 5432 (the direct `db.<ref>.supabase.co` host is IPv6-only), with TLS:
   `Host=<pooler host>;Port=5432;Database=postgres;Username=lindrake.<project ref>;Password=<password>;SSL Mode=Require`.
   Not the transaction pooler on 6543: the RLS tenant is a session-level setting made when a connection opens, which transaction pooling does not keep with the client ([DECISIONS.md](../DECISIONS.md), 2026-10-14). Session mode holds a server connection per open client connection, so the API caps its pool with `Database:MaxPoolSize` (default 10; it replaces any `Maximum Pool Size` in the connection string).
4. **Schema:** the prod overlay sets `Database__Schema=lindrake`. Tables are created unqualified and land in the role's `search_path`; `Database:Schema` puts `__EFMigrationsHistory` in the same schema, and startup migrations refuse to run when the connection's `current_schema()` is anything else.
5. **First start:** the API applies the migrations as `lindrake` (see [Database migrations](#database-migrations)); `lindrake` owns the tables and `FORCE ROW LEVEL SECURITY` binds it too.

**Moving off a deploy-host `db` container** (a host that ran the overlay before 2026-10-14): copy the data into qavren-db first (qavren-db's `docs/migration-playbook.md`). `deploy.yml` runs `up --remove-orphans`, which removes the old `db` container; its volume stays until you delete it.

See [schema-provisioning.md](./schema-provisioning.md) for what each environment's provisioning owns.

---

## Secret files

Docker file-based secrets keep sensitive values out of env vars and image layers. The API reads `/run/secrets/` via `AddKeyPerFile` in `Program.cs` (file name `Section__Key` → config key `Section:Key`), added after environment variables so a secret file wins over any environment variable of the same key.

---

## Production deploy — manual steps

```bash
# 1. Pull latest images
docker compose -f docker-compose.yml -f docker-compose.prod.yml pull web api

# 2. Restart web and api (the overlay has no db: qavren-db)
docker compose -f docker-compose.yml -f docker-compose.prod.yml \
  up -d --no-deps --remove-orphans web api

# 3. Check health
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs api --tail 50
```

First-time full start (after the qavren-db steps above):

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

### Healthchecks

| Service | Check |
|---------|-------|
| `api` | `GET /health` over bash `/dev/tcp`, healthy only on HTTP 200 (`/health` answers 503 when the database check fails). The `aspnet:10.0` runtime image has no `curl` or `wget`. |
| `web` | `wget http://127.0.0.1/app/` (not `localhost`: busybox resolves it to `::1` first and nginx listens on IPv4 only) |

---

## Environment variables

See `.env.example` for the full reference with comments.

### `.env.dev` (committed — no real secrets)

| Variable | Purpose |
|----------|---------|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | Local Postgres superuser and database (`lindrake` / `lindrake_dev` / `lindrake`) |
| `POSTGRES_APP_PASSWORD` | Password `init.sql` gives the RLS-bound `lindrake_app` role, and the one the `api` service connects with (`lindrake_dev`; `appsettings.Development.json` uses the same for a host-run API) |
| `ANTHROPIC_API_KEY` / `OPENAI_API_KEY` / `TAVILY_API_KEY` | LLM, optional OpenAI embeddings, web search |
| `JWT_SECRET` / `JWT_ISSUER` / `JWT_AUDIENCE` | HS256 identity provider settings (mapped to `Jwt__*` by `docker-compose.yml`) |
| `ASPNETCORE_ENVIRONMENT` | `Development` in dev (enables Swagger and the dev CORS origins) |
| `API_BASE_URL` | API URL as the browser sees it; rendered into the web container's `app/appsettings.json` at start |

The base compose file maps the UPPER_CASE names through `${...}` interpolation, with each value's `.env.dev` setting as the default, so local runs need no `--env-file`; pass `--env-file .env.dev` to apply edits to the file (e.g. API keys). `ASPNETCORE_ENVIRONMENT` and `API_BASE_URL` reach the containers through `env_file` either way.

### `.env.prod` (gitignored — keys come from Docker secrets)

See [.env.prod](#envprod). Sensitive values are injected via Docker file-based secrets, not env vars.

### Overriding the image tag at deploy time

The prod compose file reads `IMAGE_TAG` and `IMAGE_WEB` / `IMAGE_API` from the environment, defaulting to `latest`. To pin a specific SHA:

```bash
IMAGE_TAG=prod-3810ff9 docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

---

## Ports

| Service | Internal | Host port (base file) | With the prod overlay |
|---------|----------|-----------------------|-----------------------|
| `db` (Postgres, local only) | 5432 | 5432 | no `db` service (qavren-db) |
| `api` (ASP.NET) | 8080 | 5000 | `127.0.0.1:5000` |
| `web` (nginx) | 80 | 5080 | `127.0.0.1:5080` |

Nothing in the prod stack listens on a public interface. Put a reverse proxy (nginx, Caddy, Cloudflare Tunnel) on 80/443 and route `/api/` (and `/health` if wanted) to the API and everything else to the web container: from the host, `127.0.0.1:5000` and `127.0.0.1:5080`; from a container attached to the compose network, `api:8080` and `web:80`. The API needs a route because the web container does not proxy `/api`.

---

## Local dev (docker compose only)

`.env.dev` is committed with safe defaults; only fill in your API keys (and then add `--env-file .env.dev` to the commands below).

```bash
docker compose up -d
# API:    http://localhost:5000  (Swagger at /swagger)
# Web:    http://localhost:5080  (landing page)
# App:    http://localhost:5080/app/
```

---

## Rebuilding after code changes

```bash
docker compose build web api
docker compose up -d --no-deps web api
```

---

## Database migrations

EF Core migrations are applied automatically when the API starts: `InitializeLindrake` (`src/Lindrake.Api/Extensions/WebApplicationExtensions.cs`) validates options, then calls `LindrakeDatabase.MigrateLindrakeDatabase`, which checks that the connection's `current_schema()` is `Database:Schema` and runs `Database.Migrate()` as the connection's role (`lindrake` on qavren-db, `lindrake_app` locally). It uses `ConnectionStrings:Migrations` when set, otherwise `ConnectionStrings:DefaultConnection`; on qavren-db both are the session pooler, so the optional one is only for a different endpoint or role. A failed migration fails the boot. There is no `--migrate` flag; restarting the `api` container is how migrations are applied. The `Testing` environment skips this.

To apply them from a workstation instead (requires the `dotnet-ef` tool, which is not in a tool manifest: `dotnet tool install --global dotnet-ef`; set `Database__Schema` too when it is not `public`):

```bash
ConnectionStrings__DefaultConnection="Host=localhost;Database=lindrake;Username=lindrake_app;Password=<password>" \
  dotnet ef database update --project src/Lindrake.Api
```

---

## Embedding model (ONNX)

The API embeds text locally with **bge-small-en-v1.5** (384-dim, CLS pooling) — no
OpenAI calls, no per-token cost. `appsettings.json` ships `Embedding:Provider=Onnx`
and `InitializeLindrake` resolves the embedding service during startup, so a missing model
is a **startup crash**, not a degraded mode. That is deliberate: the alternative is
silently falling back to a paid API.

**In the image.** `src/Lindrake.Api/Dockerfile` has a `model-fetch` stage that
downloads `model.onnx` + `vocab.txt` from HuggingFace at build time, pinned to
immutable commit revisions and verified with `sha256sum`. The files land at
`/app/models/bge-small-en-v1.5/`, which is where the ContentRoot-relative
`Embedding:ModelPath` resolves. Nothing else puts the model in the image —
`models/` is gitignored (~127 MB), so it is never in the build context.

> If you rewrite the API Dockerfile, keep the `model-fetch` stage and its `COPY`.
> Dropping them produces an image that builds clean, passes every test, and then
> crash-loops on boot with `FileNotFoundException`. This happened once already;
> `DockerModelPackagingTests` now fails the build if the stage goes missing or the
> downloads lose their revision pin / checksums.

**Locally**, outside Docker, fetch the model once per machine:

```powershell
pwsh scripts/fetch-embedding-model.ps1
```

**Switching back to OpenAI is not a config flip.** `document_chunks.embedding` is
`vector(384)`; `text-embedding-3-large` emits 3072 dims. Startup refuses the
mismatch rather than letting the first insert fail — changing providers requires an
EF migration for the column first.

---

## Hosted demo — pending infra

A live demo at a public URL is planned but deferred pending infrastructure provisioning (server, domain, TLS, secrets management). Once infra is ready:

1. Provision qavren-db and set `LINDRAKE_DB_URL_PROD` ([Database (qavren-db)](#database-qavren-db)); prepare the host as described above (files, `.env.prod`, secrets).
2. Set the four `DEPLOY_*` GitHub secrets listed above.
3. Run the `deploy` workflow from the Actions tab (`workflow_dispatch`).

See `docs/marketability-roadmap.md` item 10 for tracking.
