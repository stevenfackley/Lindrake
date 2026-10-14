# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

OmniSift is a multi-tenant research SaaS: .NET 10 ASP.NET Core Web API + Blazor WebAssembly client + Postgres 16 with pgvector and pg_trgm. Users upload documents or ingest web pages; a Semantic Kernel agent answers questions over hybrid (vector + keyword) search, Tavily web search and the Wayback Machine, with citations and exportable reports. CI runs on every PR and push to `main`. `deploy.yml` (GHCR build/push + SSH deploy) is `workflow_dispatch` only; its SSH step no-ops until the `DEPLOY_*` secrets exist.

## Repo layout

- `src/OmniSift.Api/`
  - `Program.cs` composes `Extensions/ServiceCollectionExtensions.cs` (one `AddOmniSift*` module per concern) and `Extensions/WebApplicationExtensions.cs` (`InitializeOmniSift`: options validation, `Database.Migrate()`, ONNX model load; `UseOmniSiftPipeline`: middleware + endpoints).
  - `Controllers/` thin HTTP adapters. `Options/` startup-validated options + validators. `Infrastructure/` `GlobalExceptionHandler`, domain exceptions (`OmniSiftException` carries the status code), `LocalIdentityProviderConvention`, rate-limit policy names, the `Testing` environment.
  - `Data/` `OmniSiftDbContext` (tenant query filters), `TenantConnectionInterceptor` (RLS session variable), `TimestampInterceptor`, `Projections/` (EF-translated DTO projections), `Migrations/`.
  - `Services/Ingestion/` `IngestionPipeline`, SSRF-guarded `WebPageFetcher`, `OutboundAddressFilter`. `Services/Extraction/` `TextExtractorRegistry` (the only list of supported source types). `Services/Search/` `HybridSearchService`. `Services/Agent/` `ResearchAgentService`, `AgentSseWriter`, `ChatHistoryFactory`, invocation tracker, query-history recorder, built-in `ResearchProfiles`. `Services/Reports/` `ReportDocument` + Markdown/PDF renderers. Extractors, embeddings, chunker, PII scanner and the ingestion channel/worker sit directly in `Services/`.
  - `Plugins/` Semantic Kernel plugins (`VectorSearch`, `WebScraper`, `WaybackMachine`; names in `AgentPlugins`).
- `src/OmniSift.Web/` Blazor WASM. `Pages/`, `Layout/`, `Components/` (route guard, `Alert`, `AsyncStatus`), `ViewModels/` (page logic, unit-tested), `Services/` (typed `OmniSiftApiClient` returning `ApiResult`, `AuthHandler`, `AuthStateService`, `SseParser`, `ApiBaseAddress`). `docker-entrypoint.d/40-render-appsettings.sh` writes `wwwroot/appsettings.json` from `API_BASE_URL` at container start (empty = same origin).
- `src/OmniSift.Shared/` DTOs + `OmniSiftJsonContext` (source-generated, camelCase).
- `tests/OmniSift.UnitTests/` xUnit + Moq + FluentAssertions; covers API and Web (references both).
- `tests/OmniSift.IntegrationTests/` `WebApplicationFactory` in the `Testing` environment, EF Core InMemory, fake chat completion.
- `infra/db/init.sql` superuser-only setup (extensions, `omnisift_app` role, grants); EF migrations own the schema. See `docs/schema-provisioning.md`.
- `docker-compose.yml` dev stack: `db` (pgvector/pgvector:pg16), `api` (:5000), `web` (nginx :5080, landing at `/`, app at `/app/`). `docker-compose.prod.yml` overlay: GHCR images, Docker secrets, healthchecks.

## Stack details

- **.NET 10.0.103** (`global.json`, rollForward: latestFeature); `LangVersion` preview.
- **Database:** Postgres 16 + pgvector (`vector(384)`, HNSW) + pg_trgm (GIN). EF Core 10 + Npgsql; snake_case names are mapped explicitly in `OmniSiftDbContext` (no naming-convention package). Migrations run at API startup, except in `Testing`.
- **Embeddings:** local ONNX `bge-small-en-v1.5` (384-dim, CLS pooling, 512-token window) by default; the API image downloads it at build time; locally run `pwsh scripts/fetch-embedding-model.ps1`. `Embedding:Provider=OpenAi` needs a column migration first (startup refuses the dimension mismatch). Chunking is 480/48 tokens of the active model.
- **LLM:** `Anthropic:ModelId` via Semantic Kernel's OpenAI connector against `https://api.anthropic.com/v1/`. No key = agent/entity endpoints answer 503.
- **Auth:** `Supabase:Url` set = validate Supabase ES256/RS256 tokens via OIDC discovery and remove `/api/auth/*`. Empty = in-house HS256 IdP (`Jwt:Secret` ≥ 32 bytes), allowed in Development/Testing, elsewhere only with `Jwt:AllowLocalIdentityProvider=true` and a non-default secret (`JwtOptionsValidator`). Tenant comes only from the `tenant_id` claim; the default authorization policy requires it on every controller unless `[AllowAnonymous]`.
- **Tenant isolation:** EF global query filter on every `ITenantOwned` entity (unbound scope = `Guid.Empty` = no rows) + `TenantConnectionInterceptor` setting `app.current_tenant` on each connection open + RLS on `data_sources`, `document_chunks`, `query_history`, `audit_log` (append-only: no UPDATE policy). `users` has no RLS by design (HS256 register checks email uniqueness across tenants; see the `AddAuditLogRowLevelSecurity` migration); `tenants` has none either. The app must connect as `omnisift_app`, not the superuser, or RLS is bypassed.
- **Errors:** RFC 7807 ProblemDetails only. Throw an `OmniSiftException` subclass for client-facing failures; anything else becomes a generic 500.
- **Observability:** Serilog (compact JSON console) + OpenTelemetry tracing/metrics (OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, console otherwise).
- **CI:** consumes `stevenfackley/gh-actions/.github/workflows/ci-dotnet.yml@v1`, `project-name: OmniSift`, `aot-rid: ""`.
- **Lock files:** `RestorePackagesWithLockFile=true` via `Directory.Build.props`.

## Commands

Run from repo root.

```bash
# Restore (CI restores the solution; NuGet.Config is picked up either way)
dotnet restore --configfile NuGet.Config

# Build exactly as CI does (warnings are errors only because CI passes this flag)
dotnet build OmniSift.sln -c Release --no-restore -p:TreatWarningsAsErrors=true

# Test (all)
dotnet test -c Release

# Test (single — xUnit filter)
dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeTest"

# Format check (CI runs it but does not fail on it)
dotnet format --verify-no-changes --no-restore

# Full local stack. Compose defaults every interpolated value to .env.dev's;
# add --env-file .env.dev to apply edits to it (e.g. your API keys)
docker compose up --build

# Database only
docker compose up -d db
```

Running the API from the host against the compose `db`: run it with `ASPNETCORE_ENVIRONMENT=Development`; `appsettings.Development.json` connects to `localhost:5432` as `omnisift_app` with the dev password `omnisift_dev` (`POSTGRES_APP_PASSWORD` in `.env.dev`, which `init.sql` gives the role). `appsettings.json` has no connection string.

## CI/CD mental model

```
push main / PR
  │
  ▼
ci  (uses stevenfackley/gh-actions/.../ci-dotnet.yml@v1, project-name: OmniSift)
  ├─ resolve-solution
  ├─ Build & Test                       (build with -p:TreatWarningsAsErrors=true; dotnet format --verify-no-changes is continue-on-error; no Postgres service: tests use InMemory)
  ├─ Dependency & Telemetry Audit       (vulnerable packages, banned telemetry packages, secret-shape grep)
  └─ Native AOT Publish                 (skipped: aot-rid is "" — the app is not AOT-compatible, see DECISIONS.md)

workflow_dispatch only
  │
  ▼
deploy
  ├─ build-push  (GHCR omnisift-web / omnisift-api, tags latest + prod-<sha7>)
  └─ deploy      (SSH: compose pull + up web api; skipped while DEPLOY_HOST is unset)
```

## Conventions

- **Warnings as errors in CI.** Nothing in the repo sets `TreatWarningsAsErrors`; CI passes it. Build with the flag locally before pushing. CA2007 / IDE0005 / format-whitespace classes have all bitten this repo at least once — check `.editorconfig` before suppressing.
- **CA2007 scoped off in `tests/**`** — xUnit1030 forbids `ConfigureAwait` in test methods, so the rule must be off there.
- **New ingestion format** = one `ITextExtractor` registration in `AddOmniSiftIngestion` + its MIME/extension mapping in `TextExtractorRegistry`.
- **Domain-specific behaviour** (system prompt, plugin set, report title) belongs in the `ResearchProfile` config section, not in code.
- **Commits:** Conventional Commits (`feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `ci`).
- **Branches:** `main` is protected. Squash-merge via PR.
- **Lockfiles:** `packages.lock.json` commits go in the same commit as the dependency bump that triggered them.

## Don'ts

- Don't disable `TreatWarningsAsErrors` to dodge a real warning. Fix the warning or scope it via `.editorconfig` with a documented reason.
- Don't add static AWS keys or other cloud credentials. None should be needed in this repo today.
- Don't read the tenant from a header or request body; only from the validated `tenant_id` claim.
- Don't add `IgnoreQueryFilters()` without naming `OmniSiftDbContext.TenantFilter` and a comment saying why the call site must see every tenant.
- Don't return exception messages to clients; only `OmniSiftException` messages are client-facing.
