# OmniSift — Deploy Guide

Stack: ASP.NET Core Web API (`api`) + Blazor WASM served by nginx (`web`) + Postgres 16/pgvector (`db`).

---

## Routing overview

| Path | Serves |
|------|--------|
| `/` | Marketing site (`landing/`) — static HTML/CSS/JS |
| `/app/` | Blazor WebAssembly SPA (nginx rewrites to `/app/index.html`) |
| `/app/appsettings.json` | Client runtime config, rendered from `API_BASE_URL` at container start, never cached |
| `http://api:8080` | ASP.NET Core API (published on host port 5000 by the base compose file) |

The nginx config (`src/OmniSift.Web/nginx.conf`) routes `/app/` to `/usr/share/nginx/html/app/` and falls back to `index.html` for SPA deep-links. `landing/` is copied to the nginx root so `/` serves the marketing site. The web container does **not** proxy `/api`.

How the browser reaches the API is decided by `API_BASE_URL` (web container):

- **Same origin (recommended):** `API_BASE_URL=` (empty). The client calls `api/...` on the origin that served it, so the reverse proxy in front must route `/api/` (and `/health` if you want it public) to `api:8080` and everything else to `web:80`. No CORS needed.
- **Separate origin:** `API_BASE_URL=https://api.example.com`, and the API must allow the web origin: `Cors__AllowedOrigins__0=https://app.example.com` (no origins are allowed outside Development by default).

The entrypoint script rejects any other value (it must be empty or an absolute `http(s)` URL), and the container then fails to start.

---

## GHCR image names

The deploy workflow pushes to GitHub Container Registry under the repo owner's namespace:

| Service | Image |
|---------|-------|
| Web (nginx + Blazor WASM) | `ghcr.io/stevenfackley/omnisift-web` |
| API (ASP.NET Core) | `ghcr.io/stevenfackley/omnisift-api` |

Each run tags images with:
- `latest` — always the most recent build
- `prod-<7-char-SHA>` — immutable, e.g. `prod-3810ff9`

---

## CI/CD workflow

`.github/workflows/deploy.yml` runs **only on `workflow_dispatch`** (Actions → deploy → Run workflow; the `environment` input offers `production`). There is no push trigger: it was removed until infrastructure is provisioned.

### Job 1 — `build-push` (no live host needed)

1. Logs in to GHCR with the built-in `GITHUB_TOKEN` (`packages: write` permission).
2. Builds `src/OmniSift.Web/Dockerfile` (build context: repo root) → pushes as `omnisift-web`.
3. Builds `src/OmniSift.Api/Dockerfile` (build context: repo root) → pushes as `omnisift-api`.
4. Uses GitHub Actions cache (`type=gha`) per image to speed up layer reuse.

### Job 2 — `deploy` (no-op until secrets are set)

A first step checks `DEPLOY_HOST`; when it is empty the job logs that infra is not provisioned and skips the SSH step. Otherwise it SSHes to the production host and runs:

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
| `DEPLOY_WORK_DIR` | Absolute path on host where compose files live, e.g. `/srv/omnisift` |

Until these are set, the `deploy` job's SSH step is skipped; `build-push` still runs and images are pushed to GHCR.

---

## Prerequisites on the production host

Before the first SSH deploy, the following must exist on the server at `$DEPLOY_WORK_DIR`:

```
docker-compose.yml         # copied from repo
docker-compose.prod.yml    # copied from repo
.env.dev                   # copied from repo: the base compose file loads it (see below)
.env.prod                  # production overrides; do NOT commit real values
secrets/
  postgres_password.txt
  Anthropic__ApiKey.txt
  OpenAI__ApiKey.txt
  Tavily__ApiKey.txt
  Jwt__Secret.txt
  ConnectionStrings__DefaultConnection.txt
```

### How the two env files combine

`docker-compose.prod.yml` adds `.env.prod` to each service's `env_file`; Compose **appends** env_file lists, so `.env.dev` is still loaded first and `.env.prod` overrides only the keys it sets. Anything `.env.prod` leaves out keeps its dev value. Set at least:

| Key in `.env.prod` | Why |
|---|---|
| `Supabase__Url=https://<project>.supabase.co` | Production auth. The Supabase project must emit a `tenant_id` custom claim. |
| *or* `JWT__ALLOWLOCALIDENTITYPROVIDER=true` | Run the in-house HS256 provider deliberately instead. Requires a unique `Jwt__Secret` of at least 32 bytes (`secrets/Jwt__Secret.txt`); the public development default is rejected. With neither key the API refuses to start in Production. |
| `POSTGRES_PASSWORD=` (empty) | The overlay passes `POSTGRES_PASSWORD_FILE`; with `.env.dev`'s `POSTGRES_PASSWORD` also set, the postgres image exits with "both POSTGRES_PASSWORD and POSTGRES_PASSWORD_FILE are set (but are exclusive)". |
| `API_BASE_URL=` (empty) or an absolute URL | Otherwise the web container inherits `.env.dev`'s `http://localhost:5000`. |
| `Cors__AllowedOrigins__0=...` | Only when `API_BASE_URL` is a different origin from the web app. |
| `ASPNETCORE_ENVIRONMENT=Production` | The overlay already forces it for `api`; set it so `db`/`web` do not carry the dev value. |

Compose `${...}` interpolation (`IMAGE_TAG`, `IMAGE_API`, `IMAGE_WEB`, and the `${JWT_ISSUER}` / `${JWT_AUDIENCE}` mappings in the base file) reads the shell environment or a `.env` file next to the compose files, never `env_file`. `deploy.yml` passes no `--env-file`, so put such values in `$DEPLOY_WORK_DIR/.env` if you need them. In HS256 mode, set `JWT_ISSUER` and `JWT_AUDIENCE` there; otherwise `Jwt__Issuer` and `Jwt__Audience` reach the API blank.

### Populating secrets on first deploy

```bash
# Example — source from environment or your secrets manager
echo -n "$PG_PASSWORD"         > secrets/postgres_password.txt
echo -n "$ANTHROPIC_KEY"       > secrets/Anthropic__ApiKey.txt
echo -n "$OPENAI_KEY"          > secrets/OpenAI__ApiKey.txt    # may be empty with ONNX embeddings
echo -n "$TAVILY_KEY"          > secrets/Tavily__ApiKey.txt
echo -n "$(openssl rand -hex 32)" > secrets/Jwt__Secret.txt     # used only by the HS256 provider
echo -n "Host=db;Port=5432;Database=omnisift;Username=omnisift_app;Password=$APP_DB_PASSWORD" \
                                > secrets/ConnectionStrings__DefaultConnection.txt
chmod 600 secrets/*.txt
```

`secrets/*.txt` is gitignored. In automated pipelines, write these files from Vault, AWS Secrets Manager, Azure Key Vault, etc., at deploy time before running compose. Create every file the overlay lists, even one whose value is unused in your auth/embedding mode.

### First-time database setup

`infra/db/init.sql` runs once, on an empty data volume, as the superuser. It creates every extension the migrations use (`uuid-ossp`, `vector`, `pg_trgm`) and the `omnisift_app` role **with the fixed password `omnisift_dev`**, which must be replaced by hand, as the superuser, before the API starts for the first time:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d db
# wait until healthy, then (POSTGRES_USER defaults to omnisift):
docker compose -f docker-compose.yml -f docker-compose.prod.yml exec db \
  psql -U omnisift -d omnisift \
  -c "ALTER ROLE omnisift_app WITH PASSWORD '<APP_DB_PASSWORD>';"
```

The password must match the one in `ConnectionStrings__DefaultConnection.txt`. See [schema-provisioning.md](./schema-provisioning.md).

---

## Secret files

Docker file-based secrets keep sensitive values out of env vars and image layers. The API reads `/run/secrets/` via `AddKeyPerFile` in `Program.cs` (file name `Section__Key` → config key `Section:Key`), added after environment variables so the secret files win over the overlay's blanked env vars. The Postgres image reads `POSTGRES_PASSWORD_FILE` natively.

---

## Production deploy — manual steps

```bash
# 1. Pull latest images
docker compose -f docker-compose.yml -f docker-compose.prod.yml pull web api

# 2. Restart web and api (leave db running)
docker compose -f docker-compose.yml -f docker-compose.prod.yml \
  up -d --no-deps --remove-orphans web api

# 3. Check health
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs api --tail 50
```

First-time full start (after the database setup above):

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

### Healthchecks

| Service | Check |
|---------|-------|
| `db` | `pg_isready` |
| `api` | `GET /health` over bash `/dev/tcp`, healthy only on HTTP 200 (`/health` answers 503 when the database check fails). The `aspnet:10.0` runtime image has no `curl` or `wget`. |
| `web` | `wget http://127.0.0.1/app/` (not `localhost`: busybox resolves it to `::1` first and nginx listens on IPv4 only) |

---

## Environment variables

See `.env.example` for the full reference with comments.

### `.env.dev` (committed — no real secrets)

| Variable | Purpose |
|----------|---------|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | Local Postgres credentials |
| `ANTHROPIC_API_KEY` / `OPENAI_API_KEY` / `TAVILY_API_KEY` | LLM, optional OpenAI embeddings, web search |
| `JWT_SECRET` / `JWT_ISSUER` / `JWT_AUDIENCE` | HS256 identity provider settings (mapped to `Jwt__*` by `docker-compose.yml`) |
| `ASPNETCORE_ENVIRONMENT` | `Development` in dev (enables Swagger and the dev CORS origins) |
| `API_BASE_URL` | API URL as the browser sees it; rendered into the web container's `app/appsettings.json` at start |

The base compose file maps the UPPER_CASE names through `${...}` interpolation, so local runs need `docker compose --env-file .env.dev ...`.

### `.env.prod` (gitignored — keys come from Docker secrets)

See [How the two env files combine](#how-the-two-env-files-combine). Sensitive values are injected via Docker file-based secrets, not env vars.

### Overriding the image tag at deploy time

The prod compose file reads `IMAGE_TAG` and `IMAGE_WEB` / `IMAGE_API` from the environment, defaulting to `latest`. To pin a specific SHA:

```bash
IMAGE_TAG=prod-3810ff9 docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

---

## Ports

| Service | Internal | Host port (base file) | With the prod overlay |
|---------|----------|-----------------------|-----------------------|
| `db` (Postgres) | 5432 | 5432 | **still published** |
| `api` (ASP.NET) | 8080 | 5000 | **still published** |
| `web` (nginx) | 80 | 5080 | **still published** |

The overlay's `ports: []` on `db` does not remove anything: Compose merges port lists. Until the base file stops publishing them, block 5432/5000/5080 at the host firewall (or security group), put a reverse proxy (nginx, Caddy, Cloudflare Tunnel) on 80/443, and point it at `web:80` and, for same-origin API calls, `api:8080`.

---

## Local dev (docker compose only)

`.env.dev` is committed with safe defaults; only fill in your API keys.

```bash
docker compose --env-file .env.dev up -d
# API:    http://localhost:5000  (Swagger at /swagger)
# Web:    http://localhost:5080  (landing page)
# App:    http://localhost:5080/app/
```

---

## Rebuilding after code changes

```bash
docker compose --env-file .env.dev build web api
docker compose --env-file .env.dev up -d --no-deps web api
```

---

## Database migrations

EF Core migrations are applied automatically when the API starts: `InitializeOmniSift` (`src/OmniSift.Api/Extensions/WebApplicationExtensions.cs`) validates options, then calls `Database.Migrate()` as the connection-string user (`omnisift_app`). A failed migration fails the boot. There is no `--migrate` flag; restarting the `api` container is how migrations are applied. The `Testing` environment skips this.

To apply them from a workstation instead (requires the `dotnet-ef` tool, which is not in a tool manifest: `dotnet tool install --global dotnet-ef`):

```bash
ConnectionStrings__DefaultConnection="Host=localhost;Database=omnisift;Username=omnisift_app;Password=<password>" \
  dotnet ef database update --project src/OmniSift.Api
```

---

## Embedding model (ONNX)

The API embeds text locally with **bge-small-en-v1.5** (384-dim, CLS pooling) — no
OpenAI calls, no per-token cost. `appsettings.json` ships `Embedding:Provider=Onnx`
and `InitializeOmniSift` resolves the embedding service during startup, so a missing model
is a **startup crash**, not a degraded mode. That is deliberate: the alternative is
silently falling back to a paid API.

**In the image.** `src/OmniSift.Api/Dockerfile` has a `model-fetch` stage that
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

1. Prepare the host as described above (files, `.env.prod`, secrets, first-time database setup).
2. Set the four `DEPLOY_*` GitHub secrets listed above.
3. Run the `deploy` workflow from the Actions tab (`workflow_dispatch`).

See `docs/marketability-roadmap.md` item 10 for tracking.
