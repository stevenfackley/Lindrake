# Changelog

All notable changes to OmniSift will be documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

### Refactor of October 2026 (batches 1–6)

#### Behavior changes (deliberate)
- Every error response is RFC 7807 ProblemDetails (`application/problem+json`) with a `traceId`, including `401`/`403` from authentication, authorization and the tenant middleware (which answered `{"error": ...}` before). Only domain exceptions return their message; anything unexpected is a generic `500`.
- Uploads with an unknown `sourceType` (or an unmapped file type) are rejected with `400` up front instead of being accepted with `202` and failing in the background.
- A web page that downloads but fails ingestion answers `200` with `status: "failed"` instead of a `500` that also lost the data source row. `error_message` is now a short stage name, never exception text.
- `/api/health` is removed; `GET /health` now includes a database check and answers `503` when the database is unreachable.
- Outside Development/Testing the API refuses to start unless `Supabase:Url` is set or the in-house HS256 provider is enabled explicitly with `Jwt:AllowLocalIdentityProvider=true` and a unique secret of at least 32 bytes. `/api/auth/*` returns `404` when Supabase is configured.
- `GET /api/datasources` is paged: `skip`/`take`, newest first, `take` defaults to 200 (previously: all rows) with a maximum of 500.
- Agent requests: `conversationHistory` is limited to 50 messages of at most 32,000 characters with roles `user`/`assistant`; the server forwards only the most recent `Agent:MaxHistoryMessages` (default 20) to the LLM.
- The Web client no longer sends `X-Tenant-Id`; the tenant comes only from the JWT `tenant_id` claim.
- Tavily is called with `Authorization: Bearer` instead of `api_key` in the body; the Wayback Machine over https.
- CORS allows the localhost origins only in Development (they were allowed in every environment).
- Rate limiting runs after authentication and partitions on the tenant claim; previously all tenants shared one bucket.

#### Structure
- `Program.cs` split into one `AddOmniSift*` service module per concern plus `InitializeOmniSift` / `UseOmniSiftPipeline`; strongly-typed options with startup validation.
- `ResearchAgentService` owns the buffered and streamed query flow; `AgentSseWriter`, `ChatHistoryFactory`, an invocation tracker and a query-history recorder replace controller code.
- `ResearchProfile` configuration section (`Name`, `SystemPrompt`, `EnabledPlugins`, `ReportTitle`) with the family-history profile as default.
- Reports render from one `ReportDocument` through `IReportRenderer` (Markdown, PDF).
- `TextExtractorRegistry` is the single list of supported source types and MIME/extension mappings.
- Data source DTOs are projected in SQL through one mapping; ingestion status maps to its wire string in one place; `CreatedAt`/`UpdatedAt` are stamped by a `SaveChanges` interceptor.

#### Security
- Global EF tenant query filter on every tenant-owned entity (fails closed); RLS session variable set by a connection interceptor on every connection open; default authorization policy requiring the `tenant_id` claim on every controller.
- Row-level security on `audit_log` (append-only: no UPDATE policy); audit entries record the acting user.
- SSRF guard for web ingestion (public addresses only, checked per connection and redirect) with a 10 MB body cap and 30-second timeout.
- Search query text is no longer logged by the kernel plugins; over-long upload fields are rejected before they reach the database.
- vis-network is loaded with a pinned version and Subresource Integrity.

#### Correctness
- Uploads and web ingestion run through one `IngestionPipeline` that always records `completed` or `failed`.
- Chunks are sized in tokens of the embedding model (480/48 by default), so embeddings see the whole chunk.
- Hybrid search's keyword arm searches all of the tenant's chunks, independent of the vector hits.
- The agent's citations are persisted in `query_history.sources`.
- Account erase deletes set-based in one transaction and audits the erase.
- Quoted CSV newlines are kept in SMS exports; script text is dropped from email HTML; extractor input is buffered only when the stream cannot seek.
- The JSON source-generation context is registered for MVC; typed HTTP clients configure base addresses and auth in one place.
- A fatal startup error exits with code 1.
- Plain-text uploads (`.txt`, `.md`) are supported.

#### Web client
- Typed `HttpClient` with a bearer-token handler; one request path returning `ApiResult` with ProblemDetails messages.
- The API base URL is read at container start from `API_BASE_URL` (empty = same origin) instead of being baked into the build.
- Route guard over the JWT auth state, including token expiry.
- `DataSourcesViewModel` (paging, upload status polling) and `AgentChatViewModel` (Stop, error frames, no silent fallback re-run).
- Spec-compliant SSE parser; `Alert` and `AsyncStatus` components.

#### Tests
- Tautological tests removed; new unit and integration coverage for the query filter, connection interceptor, `audit_log` RLS migration, options validators, ingestion pipeline, SSRF filter, hybrid search, agent service and SSE writer, and the Web client's services and view models. CI runs the full unit and integration suites.

#### Docs and ops
- README, CLAUDE.md, SDD, PDD, deploy guide, schema provisioning notes, roadmap, `.env.example` and ADRs brought in line with the code.
- Prod compose healthchecks fixed: the API image has no `curl`, and busybox `wget` could not reach nginx via `localhost`.
- Dependabot moved to the monthly, grouped schedule with at most 3 open PRs per ecosystem.

### Added (initial scaffold)
- Initial project scaffold — ASP.NET Core API, Blazor WebAssembly frontend, shared DTOs
- Multi-tenant architecture with PostgreSQL Row-Level Security
- Semantic Kernel agent with VectorSearch, WebScraper, and WaybackMachine plugins
- Document ingestion pipeline for PDF, SMS exports, and web pages
- pgvector-backed semantic search with HNSW indexing
- GitHub Actions CI pipeline (build + test)
- Unit tests and integration tests with an in-memory database
