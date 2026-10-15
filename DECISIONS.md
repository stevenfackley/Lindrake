# Decisions

ADR log. One entry per architectural decision. Append-only; supersede with a new entry.

## Format

```
## {{DATE}} — {{title}}
**Status:** proposed | accepted | superseded by #N
**Context:** why we had to decide
**Decision:** what we chose
**Consequences:** what follows (pros, cons, risks)
```

---

## 2026-04-17 — Initial stack: .NET 10 Native AOT

**Status:** superseded by [2026-10-14 — Native AOT is not a target](#2026-10-14--native-aot-is-not-a-target). Added by the workspace baseline sweep from the `repo-template-dotnet10-aot` template; Lindrake never published with AOT.
**Context:** Greenfield service under portfolio `repo-template-dotnet10-aot`. Target: fast cold-start, small image, Linux deploys.
**Decision:** .NET 10 with `PublishAot=true`, `linux-musl-x64`, distroless static runtime.
**Consequences:**
- Cold start < 100ms, image ~15MB.
- Reflection, dynamic code gen restricted — must stay AOT-compatible.
- No Application Insights SDK (banned by CI); stdout logs only.

---

## 2026-06-30 — Supabase ES256 auth + tenant derived from the JWT claim

**Status:** accepted. The HS256 fallback described here is amended by [2026-10-14 — HS256 local identity provider: Development/Testing by default, explicit opt-in elsewhere](#2026-10-14--hs256-local-identity-provider-developmenttesting-by-default-explicit-opt-in-elsewhere).
**Context:** Tenant identity was taken from the client-supplied `X-Tenant-Id` header (and an `X-API-Key` middleware). The header won over the JWT claim, so any authenticated caller could read another tenant's data by changing the header — a tenant-spoofing hole. The portfolio standard (square-log / SquareLog) is Supabase-issued ES256 JWTs validated via OIDC discovery, no symmetric secret.
**Decision:**
- Validate Supabase ES256/RS256 access tokens via OIDC discovery (`MetadataAddress = {Supabase:Url}/auth/v1/.well-known/openid-configuration`, audience `authenticated`, `MapInboundClaims=false`). Enabled by setting `Supabase:Url`. No symmetric secret in this path.
- Tenant is derived ONLY from the validated `tenant_id` claim (`ClaimsPrincipalExtensions.TryGetTenantId`). The `X-Tenant-Id` header is no longer read for authorization. The Supabase project must emit `tenant_id` as a custom claim (app_metadata / access-token hook).
- Data controllers are `[Authorize]`; health + `/api/auth` stay anonymous. The `X-API-Key`/`X-Tenant-Id` middleware (including the global-key impersonation path) was removed.
- The in-house HS256 IdP (`AuthController` register/login + `JwtTokenService`) is retained as a **development-only fallback** used when `Supabase:Url` is empty; integration tests run on it. It is slated for removal once the Blazor frontend migrates to Supabase auth.
**Consequences:**
- Closes the tenant-spoofing hole; spoofing is covered by integration tests.
- Production requires a Supabase project + a `tenant_id` custom claim. The Blazor frontend still logs in against the in-house IdP — its migration to Supabase is a follow-up (it already sends Bearer; the stale `X-Tenant-Id` header it adds is now ignored).

---

## 2026-06-30 — Defer at-rest encryption of PII columns

**Status:** accepted (deferred)
**Context:** The SaaS stores personal docs / SMS bodies. The primary PII column is `document_chunks.content`. Transparent EF value-converter encryption was considered for this PR.
**Decision:** Defer column encryption. `document_chunks.content` is backed by a GIN `pg_trgm` index (`ix_document_chunks_content_trgm`) that powers the hybrid keyword-search arm (`word_similarity`, owned by `Services/Search/HybridSearchService`; corrected 2026-10-14, this entry originally named `VectorSearchPlugin` and an ILIKE arm). Encrypting the column at rest makes the ciphertext unsearchable and silently breaks keyword search. A correct solution (searchable/deterministic encryption, tokenization, or dropping the keyword arm in favour of the vector arm) is a design decision of its own and is out of scope for the auth PR.
**Consequences:**
- Auth + tenant-isolation ship now; PII-at-rest encryption tracked as a follow-up.
- Interim mitigations already present: tenant RLS, PII detection flags (`PiiScanner`), and TLS in transit.

---

## 2026-10-14 — Native AOT is not a target

**Status:** accepted. Supersedes [2026-04-17 — Initial stack: .NET 10 Native AOT](#2026-04-17--initial-stack-net-10-native-aot).
**Context:** The 2026-04-17 entry came from a template and describes a stack Lindrake never had: no project sets `PublishAot`, the API image is the JIT `mcr.microsoft.com/dotnet/aspnet:10.0` runtime (Ubuntu 24.04), not a distroless static one. The shared CI's AOT lane fell back to the Blazor WASM project and failed with NETSDK1203, so `ci.yml` has passed `aot-rid: ""` since 2026-09-22 (#87) and the job is skipped.
**Decision:** Ship the API as a regular JIT ASP.NET Core app. Do not gate dependency or design choices on AOT compatibility. What blocks AOT today:
- Options validation uses reflection-based `ValidateDataAnnotations()` (the `[OptionsValidator]` source generator would be the AOT-safe replacement).
- Semantic Kernel imports plugins by reflection (`ImportPluginFromObject`).
- EF Core with Npgsql + pgvector builds its model and queries at runtime.
- QuestPDF renders through a native graphics dependency and has not been verified under AOT; Swashbuckle generates its document by reflection.
**Consequences:**
- No cold-start or image-size gains from AOT; the API image also carries the ~127 MB ONNX model regardless.
- The CI AOT lane stays skipped; re-enabling it means fixing every item above first.
- `LindrakeJsonContext` (source-generated JSON) stays: it is useful without AOT.

---

## 2026-10-14 — HS256 local identity provider: Development/Testing by default, explicit opt-in elsewhere

**Status:** accepted. Amends the HS256 part of the 2026-06-30 Supabase entry.
**Context:** The in-house HS256 IdP was selected whenever `Supabase:Url` was empty, so a deployment that forgot `Supabase__Url` (the prod compose overlay sets none) silently issued and accepted self-signed tokens, with a default secret from `appsettings.json` as fallback. Deployments without a Supabase project still need a way to run, deliberately.
**Decision:**
- `JwtOptionsValidator` (validated on start) fails startup when `Supabase:Url` is empty outside Development/Testing, unless `Jwt:AllowLocalIdentityProvider=true` (env `JWT__ALLOWLOCALIDENTITYPROVIDER`).
- While HS256 is active, `Jwt:Secret` must be at least 32 bytes; outside Development/Testing it must also differ from the public development default, which now lives only in `appsettings.Development.json`.
- `LocalIdentityProviderConvention` removes `AuthController` when Supabase is configured, so `/api/auth/*` is a 404 instead of minting tokens nothing accepts.
**Consequences:**
- A misconfigured production deployment refuses to start with a message naming the flag, instead of running an unintended IdP.
- Running HS256 in production is a visible, reviewable configuration choice; its secret handling is the operator's responsibility.
- Integration tests keep using HS256 in the `Testing` environment.

---

## 2026-10-14 — `ResearchProfile` as the configuration seam for the domain

**Status:** accepted
**Context:** The agent's system prompt, plugin set and default report title were hard-coded for family-history research. Other domains (legal, claims, investigations) would each have needed a code change.
**Decision:** `ResearchProfileOptions` (section `ResearchProfile`: `Name`, `SystemPrompt`, `EnabledPlugins`, `ReportTitle` with `{profile}` and `{date}` tokens) drives `ChatHistoryFactory`, the scoped kernel's plugin imports and `ReportRequestFactory`. Unset values fall back to `ResearchProfiles.FamilyHistory`. `ResearchProfileOptionsValidator` rejects unknown or duplicate plugin names at startup; `EnabledPlugins` is nullable (absent = all) because the configuration binder appends to pre-populated lists.
**Consequences:**
- A new domain is a configuration file, not a code change; an unconfigured deployment behaves as before.
- New plugins must be added to `AgentPlugins` and `ResolvePlugin` before a profile can enable them.
- Arrays merge index by index across configuration sources, so a profile file cannot remove a plugin listed by an earlier source; `appsettings.json` therefore does not set `EnabledPlugins`.

---

## 2026-10-14 — Tenant isolation lives in the data layer

**Status:** accepted
**Context:** Isolation relied on RLS plus a middleware that pinned one connection per request to set `app.current_tenant` (held through long LLM calls, lost on a retry's reconnect) and on a hard-coded list of paths that needed a tenant. Application queries had no tenant predicate of their own, and `audit_log` had no RLS.
**Decision:**
- EF Core global query filter (named `Tenant`) on every `ITenantOwned` entity, reading the scope's `ITenantContext`. An unbound scope compares with `Guid.Empty` and matches nothing: fail closed, not throw. Bypassing it requires `IgnoreQueryFilters([LindrakeDbContext.TenantFilter])` with a stated reason.
- `TenantConnectionInterceptor` runs `set_config('app.current_tenant', ...)` each time EF opens a connection for a bound context, so RLS applies to every (re)opened connection and none is held between queries.
- The default authorization policy (authenticated + `tenant_id` claim) applies to every controller via `MapControllers().RequireAuthorization()`; `[AllowAnonymous]` opts out. It is not a fallback policy, which would turn unmatched-route 404s into 401s.
- `audit_log` gets RLS with SELECT, INSERT and DELETE policies and no UPDATE policy: append-only even for the owner; DELETE remains for the account erase.
- `users` deliberately has no RLS: HS256 registration must check email uniqueness across all tenants before a tenant exists, which FORCE RLS would hide (a duplicate would surface as a 500 instead of a 409). The table only backs the in-house IdP.
**Consequences:**
- A forgotten filter in code is still caught by RLS, and a connection used without RLS (InMemory tests, a superuser) is still filtered by EF.
- The background worker must bind `ITenantContext` from the work item before any query, which it does.
- The app must connect as the non-superuser `lindrake_app`; a superuser connection bypasses RLS silently.

---

## 2026-10-14 — RFC 7807 ProblemDetails is the only error shape

**Status:** accepted
**Context:** `GlobalExceptionHandler` turned every `InvalidOperationException` into a 422 and every `ArgumentException` into a 400 and returned `exception.Message`, including on 500s, leaking internals. The tenant middleware's 403 answered `{"error": ...}` while controllers answered ProblemDetails.
**Decision:** Every error response is ProblemDetails (`application/problem+json`) with a `traceId`. Client-facing failures are `LindrakeException` subclasses carrying their status (`UnsupportedSourceTypeException`, `UnsupportedFileTypeException`, `BlockedUrlException` → 400, `TenantContextMissingException` → 403, `LlmNotConfiguredException` → 503); only their messages are returned. `BadHttpRequestException` keeps its status, `HttpRequestException` is a generic 502, anything else a generic 500 logged at Error. Controllers use `Problem()`; the tenant middleware's 403 and the authentication/authorization 401/403 responses carry ProblemDetails bodies too.
**Consequences:**
- One parser on the client (`ApiProblemReader`); messages are safe to show.
- Throwing a plain exception for an expected condition yields a 500 by design; new client-facing failures need an `LindrakeException` subclass.

---

## 2026-10-14 — One ingestion pipeline, in-memory channel worker for uploads

**Status:** accepted
**Context:** Uploads (background) and web ingestion (synchronous) duplicated extract → PII scan → chunk → embed → persist, each inside a transaction that the failure paths never committed: failed rows sat in `processing` forever, and a failed web ingestion lost its row while returning its id. Raw exception messages reached `error_message`.
**Decision:**
- `IngestionPipeline` serves both paths. Extraction, PII scan, chunking and embedding run outside any transaction; chunk inserts and the `completed` status go out in one `SaveChanges`. On failure the change tracker is cleared and only `failed` plus a client-safe stage name is saved; the exception is logged. Cancellation also records `failed`.
- Uploads are validated (source type via `TextExtractorRegistry`, size, name length) before a row exists, then queued on `IngestionChannel`, a bounded in-memory `System.Threading.Channels` queue (capacity 100), and drained by `IngestionBackgroundService` in a fresh DI scope per item bound to the item's tenant.
- Web ingestion commits its row first, so a pipeline failure answers 200 with `status: "failed"`.
**Consequences:**
- Every row ends `completed` or `failed`, with a message that is safe to show.
- Trade-off: the queue is in-memory. Items queued but not yet processed are lost when the API restarts and their rows stay `pending`; there is no retry or re-enqueue on startup. A durable queue (or re-enqueueing `pending` rows at startup) is a known follow-up.
- Upload bytes are buffered in memory until processed (up to 100 queued items of up to 50 MB each).

---

## 2026-10-14 — Renamed OmniSift to Lindrake

**Status:** accepted
**Context:** "OmniSift" collides with an existing product. The project is also changing course: from a personal and family-history research assistant to a horizontal records and evidence platform, where family history becomes one research profile among others (the `ResearchProfile` seam above). The old name tied the product to its first use case.
**Decision:** Rename the product to **Lindrake**, a coinage: *lind* (Old English for the linden tree, and the linden-wood shield), *rake* (to comb through), and *drake* (Old English *draca*, dragon). The name was vetted clean on .com, .io, GitHub and NuGet. Still owed by the owner: a trademark search, and registering `lindrake.com` (pending; until it is registered, nothing in the tree points at it).
**Consequences:**
- Code: `Lindrake.sln`, project directories, assemblies and namespaces `Lindrake.*`; product-named types follow (`LindrakeDbContext`, `LindrakeJsonContext`, `LindrakeException`, `LindrakeApiClient`, the `AddLindrake*` modules). Migration IDs, migration bodies and `__EFMigrationsHistory` rows are unchanged; only namespaces and CLR entity names in the designer and snapshot files moved, and the model reports no pending changes.
- Database: superuser and database `lindrake`, application role `lindrake_app`, test database `lindrake_test`, dev password `lindrake_dev`. `init.sql` runs only on an empty volume, so every existing dev volume has to be recreated. There is no deployed database to migrate; one would need its role and database renamed by hand.
- Compose pins `name: lindrake`, so container (`lindrake-db/-api/-web`), network and volume (`lindrake_postgres_data`) names no longer follow the checkout folder.
- Images: `ghcr.io/<owner>/lindrake-web` and `lindrake-api`; the `omnisift-*` packages get no new tags.
- Auth: JWT issuer and audience defaults are `Lindrake` (`appsettings.json`, `JwtOptions`) and `https://lindrake.local` / `lindrake-api` (compose, `.env` files), so tokens minted under the old values are rejected. The Blazor client's localStorage keys are `lindrake_*`, so browsers sign in again.
- Observability: OpenTelemetry service name `Lindrake.Api`; outbound User-Agent `Lindrake/1.0`.
- Kept as is: the embedding parity fixture `bge_small_en_v1_5_reference_vectors.json` and `tools/generate_reference_vectors.py` still contain the sentence "OmniSift is a multi-tenant AI research assistant…", because the expected vectors are the embedding of that exact text. The script's usage comment also still names the old `tests/OmniSift.UnitTests` output path.
- The GitHub repository and the local folder are renamed separately, after this change merges. The README badge and clone URLs already point at `stevenfackley/Lindrake` and resolve once the repository is renamed.

---

## 2026-10-14 — qavren-db as the data platform, session pooling, Postgres test lane

**Status:** accepted.
**Context:** Production was to run its own Postgres container from the prod compose overlay, with a manual role-password step, and nothing in CI touched a real database: the integration suite runs on EF InMemory, which has no RLS, no pgvector or pg_trgm, no foreign-key cascades and no `ExecuteDelete`. The workspace consolidates app data on qavren-db: one hosted Supabase Pro Postgres, one schema and one login role per app, Postgres grants as the wall between apps, extensions project-wide in the `extensions` schema. qavren-db recommends the transaction pooler (`:6543`) for runtime traffic and session mode (`:5432`) for migrations.
**Decision:**
- Deployed data lives in qavren-db: schema `lindrake`, role `lindrake` (no superuser, no BYPASSRLS, `search_path = lindrake, extensions`), provisioned by `qavren-db/tools/provision-app.ps1`. The prod overlay no longer runs Postgres; `deploy.yml` writes the API's connection-string secret from `LINDRAKE_DB_URL_PROD` (`LINDRAKE_DB_URL_TEST` for a test environment). Local development keeps the compose `db` and `init.sql`.
- **Session mode for runtime and migrations** (`:5432` on the pooler host, which also works over IPv4; `SSL Mode=Require`). `TenantConnectionInterceptor` binds the RLS tenant with a session-level `set_config` when a connection opens. Under transaction pooling each transaction may run on a different server connection: the setting would stay on whichever server connection ran it, where another client's next transaction inherits it, while this client's next query can land on a server connection carrying someone else's tenant. Migrating at startup needs session mode too (qavren-db migration playbook, 5b). Session mode pins one server connection per open client connection, so `Database:MaxPoolSize` (default 10) replaces `Maximum Pool Size` on every connection string the API uses.
- **Schema:** no `HasDefaultSchema`. Adding it changes the model (checked: `HasDefaultSchema("public")` alone makes `HasPendingModelChanges()` true and `Migrate()` refuse), and EF would generate a migration moving every table. Certavoice started on qavren-db with `HasDefaultSchema("certavoice")` in its first migration and `MigrationsHistoryTable("__ef_migrations_history", "certavoice")`; Lindrake gets the same placement without rewriting its history: the unqualified migrations land in the role's `search_path`, and `Database:Schema` (default `public`; `lindrake` in the prod overlay) puts `__EFMigrationsHistory` in that schema and is checked against `current_schema()` before startup migrations run. The optional `ConnectionStrings:Migrations` gives startup migrations their own connection.
- **Test lane instead of Testcontainers:** `tests/Lindrake.IntegrationTests/Postgres` runs against the server in `ConnectionStrings__PostgreSQL` (CI: the shared lane's `run-postgres` service on `pgvector/pgvector:pg16`; locally: the compose `db`) and is skipped without it. One database per test class, provisioned like the compose `db` or like qavren-db, migrated by the API's own startup code as a non-superuser role, dropped `WITH (FORCE)` afterwards. No container management in the tests, no new packages.
**Consequences:**
- The deploy host runs no database; qavren-db's backups and restore drill cover the data. The extensions the migrations need (`vector`, `pg_trgm`, `uuid-ossp`) must be enabled in qavren-db's `extensions` schema and declared in its `apps/lindrake.yaml`, since the role cannot create them.
- Each API instance holds up to `Database:MaxPoolSize` server connections on the session pooler.
- CI exercises RLS, the interceptor on a reused connection, pgvector and pg_trgm search, cascades and migrations in both layouts. Its first run found the `InitialCreate` policies failing with 22P02 on a pool-reset connection (fixed by `FailClosedTenantPolicies`).
- `ci.yml` depends on the `postgres-image` input of `stevenfackley/gh-actions` `ci-dotnet.yml@v1`.
