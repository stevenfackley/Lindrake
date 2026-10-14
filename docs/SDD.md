# OmniSift — Software Design Document (SDD)

**Version:** 2.0
**Date:** 2026-10-14 (first issued 2026-02-25)
**Status:** Active Development

---

## Revision 2026-10-14

The 1.0 document described the February 2026 plan. This revision brings it in line with the code after the October 2026 refactor (batches 1–6). What changed:

- **Runtime:** .NET 8 / C# 12 → .NET 10 (C# 14, `LangVersion` preview), EF Core 10, Semantic Kernel 1.80.
- **Embeddings:** OpenAI `text-embedding-3-large` / `VECTOR(3072)` → local ONNX `bge-small-en-v1.5`, `vector(384)`. OpenAI remains an opt-in provider that needs a column migration.
- **Chunking:** whitespace-approximated 500/50 → 480/48 tokens counted with the embedding model's own tokenizer.
- **Search:** cosine-only vector search → hybrid search (pgvector + pg_trgm, fused by Reciprocal Rank Fusion) in `HybridSearchService`.
- **Schema:** 3 tables → 6 (`tenants`, `data_sources`, `document_chunks`, `query_history`, `audit_log`, `users`).
- **Auth:** the "placeholder" JWT and `X-Tenant-Id` header are gone. Supabase-issued ES256/RS256 tokens in production; an in-house HS256 identity provider in Development/Testing or behind an explicit opt-in. Tenant comes only from the `tenant_id` claim.
- **Tenant isolation:** EF global query filters + a connection interceptor that sets the RLS session variable + RLS policies (now also on `audit_log`) + a default authorization policy.
- **Ingestion:** one pipeline for uploads (async, channel worker) and web pages (sync, SSRF-guarded); formats come from an extractor registry and now include DOCX, email and plain text. CsvHelper was never used: the SMS CSV parser is hand-written.
- **Agent:** query flow moved into `ResearchAgentService` with SSE streaming, invocation tracking and persisted citations; the persona is a `ResearchProfile` configuration section. Web search is Tavily only (no Bing).
- **API:** endpoints for web ingestion, streaming, reports, entity graphs, account export/erase, audit log and HS256 auth; RFC 7807 ProblemDetails for every error; `/health` with a database check; paged data source list.
- **Web client:** typed API client, bearer-token handler, route guard, view models and a runtime API base URL.
- **Tests:** test class names in §9 replaced with the real ones.

---

## 1. System Overview

OmniSift is a containerized, multi-tenant SaaS application on .NET 10. A Blazor WebAssembly client (served by nginx) talks to an ASP.NET Core Web API; AI orchestration is Microsoft Semantic Kernel; storage is PostgreSQL with pgvector and pg_trgm.

### High-Level Architecture

```
┌────────────────────────┐   HTTPS/JSON + Bearer JWT   ┌──────────────────────────────────┐
│  nginx (web)           │ ◄─────────────────────────► │  ASP.NET Core Web API (api)      │
│  /      landing page   │   (tenant_id claim)         │  :8080 (host :5000 in dev)       │
│  /app/  Blazor WASM    │                             │                                  │
│  :80 (host :5080 dev)  │                             │  ┌────────────────────────────┐  │
└────────────────────────┘                             │  │ ResearchAgentService (SK)  │  │
                                                       │  │  VectorSearch → Hybrid     │  │
        Supabase Auth (prod) ── OIDC/JWKS ──────────►  │  │  WebScraper  → Tavily      │  │
                                                       │  │  WaybackMachine → archive  │  │
                                                       │  └────────────────────────────┘  │
                                                       │  ┌────────────────────────────┐  │
                                                       │  │ IngestionPipeline          │  │
                                                       │  │  upload → channel worker   │  │
                                                       │  │  web URL → SSRF-guarded    │  │
                                                       │  └────────────────────────────┘  │
                                                       └───────────────┬──────────────────┘
                                                                       │ omnisift_app role
                                                               ┌───────▼─────────┐
                                                               │ PostgreSQL 16   │
                                                               │ pgvector, pg_trgm│
                                                               │ RLS enabled     │
                                                               └─────────────────┘
```

## 2. Technology Stack

| Component | Technology | Version |
|---|---|---|
| Runtime | .NET | 10 (SDK 10.0.103, `global.json`) |
| Language | C# | 14 (`LangVersion` preview) |
| Frontend | Blazor WebAssembly | .NET 10 |
| Backend | ASP.NET Core Web API | .NET 10 |
| AI Orchestration | Microsoft Semantic Kernel | 1.80 |
| ORM | Entity Framework Core + Npgsql | 10.x |
| Database | PostgreSQL | 16 (`pgvector/pgvector:pg16`) |
| Vector / keyword search | pgvector (HNSW), pg_trgm (GIN) | Extensions |
| Document parsing | PdfPig, HtmlAgilityPack, DocumentFormat.OpenXml, MimeKit | See `OmniSift.Api.csproj` |
| Embeddings | ONNX Runtime + Microsoft.ML.Tokenizers, `bge-small-en-v1.5` | Local model |
| LLM Provider | Anthropic Claude (`Anthropic:ModelId`, default `claude-sonnet-4-20250514`) via SK's OpenAI connector | API |
| Web search | Tavily | API |
| PDF reports | QuestPDF (Community license) | 2025.7 |
| Auth | JwtBearer (Supabase OIDC or HS256), BCrypt.Net-Next | — |
| Observability | Serilog, OpenTelemetry | — |
| Containerization | Docker + Docker Compose | — |

## 3. Multi-Tenancy Architecture

### 3.1 Strategy: Shared Database, Shared Schema with RLS

OmniSift uses a **shared database, shared schema** multi-tenancy model. Isolation is enforced in the application (EF Core global query filters, authorization policy) and in the database (PostgreSQL Row-Level Security).

**Why this approach:**
- Cost-efficient — single database instance serves all tenants
- Simplified operations — one schema to migrate and maintain
- Defence in depth — a missing filter in code is still caught by RLS, and vice versa
- Scalable — can shard later if needed

### 3.2 Tenant Resolution Flow

```
1. HTTP request arrives with a Bearer JWT carrying a tenant_id claim
2. Authorization requires an authenticated identity with that claim (default
   policy on every controller; [AllowAnonymous] on auth + health)
3. TenantMiddleware binds the request's ITenantContext to the claim
4. EF applies a global tenant query filter to every ITenantOwned entity
5. TenantConnectionInterceptor runs set_config('app.current_tenant', ...) on
   every connection EF opens
6. RLS policies filter every query as the second line of defence
7. Response returned to client
```

The background ingestion worker does the same binding (step 3) itself: each work item carries its tenant id, and the worker sets the scope's `ITenantContext` before any query.

### 3.3 Row-Level Security Policies

RLS is enabled and forced on `data_sources`, `document_chunks`, `query_history` (InitialCreate) and `audit_log` (`AddAuditLogRowLevelSecurity`). The original tables use one policy per table:

```sql
ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_{table} ON {table}
    FOR ALL
    USING (tenant_id = current_setting('app.current_tenant', true)::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant', true)::uuid);
```

`audit_log` has separate SELECT, INSERT and DELETE policies and no UPDATE policy, so the audit trail is append-only even for the table owner (DELETE stays for the account erase). Its policies read the setting through `NULLIF(..., '')`, so an unbound connection matches no rows.

`users` deliberately has no RLS: HS256 registration must check email uniqueness across all tenants before a tenant exists. `tenants` has no RLS and no query filter.

RLS only applies because the API connects as the non-superuser `omnisift_app` role (`infra/db/init.sql`); a superuser bypasses it.

### 3.4 Database Schema

Owned by EF Core migrations (`src/OmniSift.Api/Data/Migrations/`); column names are snake_case, mapped explicitly in `OmniSiftDbContext`.

```
tenants
├── id (UUID, PK)
├── name (VARCHAR 256), slug (VARCHAR 128, unique)
├── api_key_hash (VARCHAR 64, unique)
├── is_active (BOOLEAN)
└── created_at, updated_at (TIMESTAMPTZ)

data_sources
├── id (UUID, PK)
├── tenant_id (UUID, FK → tenants)
├── source_type (VARCHAR 50: a registered extractor key, e.g. pdf, sms, web, docx, email)
├── file_name (VARCHAR 512), original_url (VARCHAR 2048)
├── status (VARCHAR 50: pending, processing, completed, failed)
├── error_message (TEXT, client-safe stage name)
├── metadata (JSONB), has_pii (BOOLEAN), pii_flags (JSONB)
└── created_at, updated_at (TIMESTAMPTZ)

document_chunks
├── id (UUID, PK)
├── tenant_id (UUID, FK → tenants)
├── data_source_id (UUID, FK → data_sources, cascade)
├── content (TEXT, GIN pg_trgm index)
├── chunk_index (INTEGER), token_count (INTEGER)
├── embedding (VECTOR(384), HNSW cosine index)  -- bge-small-en-v1.5
├── metadata (JSONB)
└── created_at, updated_at (TIMESTAMPTZ)

query_history
├── id (UUID, PK)
├── tenant_id (UUID, FK → tenants)
├── query_text (TEXT), response_text (TEXT)
├── plugins_used (JSONB), sources (JSONB citations)
├── duration_ms (INTEGER)
└── created_at (TIMESTAMPTZ)

audit_log
├── id (UUID, PK)
├── tenant_id (UUID, FK → tenants), user_id (UUID)
├── action (VARCHAR 128), resource_type (VARCHAR 128), resource_id (UUID)
├── ip_address (VARCHAR 64)
└── created_at (TIMESTAMPTZ)

users            -- backs the in-house HS256 identity provider only
├── id (UUID, PK)
├── tenant_id (UUID, FK → tenants)
├── email (VARCHAR 256, unique across tenants)
├── password_hash (TEXT, BCrypt), role (VARCHAR 64)
└── created_at (TIMESTAMPTZ)
```

`CreatedAt`/`UpdatedAt` are stamped by `TimestampInterceptor` on save. See [schema-provisioning.md](./schema-provisioning.md) for what `init.sql` owns versus EF.

## 4. Data Ingestion Pipeline

### 4.1 Pipeline Architecture

```
Upload (multipart, ≤ 50 MB)                      Web URL
  │ resolve source type (registry), 400 if none     │ SSRF-guarded fetch (≤ 10 MB, 30 s, ≤ 5 redirects)
  │ insert data_sources row (pending)               │ insert data_sources row (processing)
  │ enqueue IngestionWorkItem → 202 Accepted        │
  ▼                                                 │
IngestionChannel (bounded, 100, in-memory)          │
  ▼                                                 │
IngestionBackgroundService                          │
  │ new DI scope, bind tenant, status processing    │
  ▼                                                 ▼
IngestionPipeline.RunAsync: extract → PII scan → chunk → embed (batches of 20) → persist
  │ chunks + Completed status in one SaveChanges (one transaction)
  └ any failure: discard staged changes, record Failed + client-safe stage message
```

| Component | Responsibility |
|---|---|
| `ITextExtractorRegistry` / `TextExtractorRegistry` | The single list of supported source types; maps an upload's MIME type (or, for missing/`application/octet-stream` MIME types, its extension) to a source type |
| `ITextExtractor` | One per source type: raw text from the format |
| `PiiScanner` | Flags PII types on the data source; never rejects a document |
| `ITextChunker` / `TextChunker` | Token-budgeted, overlapping chunks |
| `ITokenCounter` | The ONNX model's own tokenizer; a words × 1.3 heuristic for OpenAI |
| `IEmbeddingService` | `OnnxEmbeddingService` (default) or `OpenAIEmbeddingService` |
| `IIngestionPipeline` / `IngestionPipeline` | Runs one data source end to end; never throws for pipeline failures, returns the row as Completed or Failed |
| `IngestionChannel` + `IngestionBackgroundService` | Async upload queue and its single reader |
| `IWebPageFetcher` / `WebPageFetcher` | Web ingestion download with the SSRF guard (`OutboundAddressFilter`) in the socket connect callback |

Known limitation: the channel is in-memory, so uploads that are queued but not yet processed are lost if the API restarts; their rows stay `pending`. A durable queue is a follow-up (DECISIONS.md, 2026-10-14).

### 4.2 Text Extraction Strategies

| Source Type | Library | Extraction Strategy |
|---|---|---|
| **pdf** | PdfPig | Page by page, concatenated with page markers |
| **sms** (CSV) | Hand-written parser | Header-mapped columns (`sender`/`from`/…, `timestamp`/`date`/…, `message`/`body`/…); quoted fields may contain newlines |
| **sms** (JSON) | System.Text.Json | Array of `{ sender, timestamp, message }` objects |
| **web** (HTML) | HtmlAgilityPack | Drops scripts, styles, navigation and form elements; prefers `<article>`, `<main>`, `role=main` and content-class containers |
| **docx** | DocumentFormat.OpenXml | Body paragraphs, paragraph breaks preserved |
| **email** (`.eml`, `.mbox`) | MimeKit (+ HtmlAgilityPack for HTML bodies) | Per message: subject, from, to, date, body; `.msg` rejected |
| plain text (`.txt`, `.md`) | — | Plain-text extractor (added in batch 6) |

Adding a format is one `ITextExtractor` registration in `AddOmniSiftIngestion` plus its MIME/extension mapping in `TextExtractorRegistry`.

### 4.3 Chunking Algorithm

```
Input: raw text, MaxTokens = 480, OverlapTokens = 48 (Chunking section)
Output: List<TextChunk>

1. Split into units: lines and . ! ? terminated sentences
2. Pack whole units into a chunk until the next would exceed MaxTokens
   (tokens counted by ITokenCounter, i.e. the embedding model's tokenizer)
3. A unit larger than the budget is packed word by word
4. The next chunk starts with whole trailing units of the previous one,
   up to OverlapTokens
5. Each chunk gets a sequential index and its token count
```

480 + the `[CLS]`/`[SEP]` tokens fits `bge-small-en-v1.5`'s 512-token window, so nothing is truncated before embedding. Startup validation requires `OverlapTokens < MaxTokens`.

### 4.4 Embeddings

`OnnxEmbeddingService` loads one thread-safe ONNX Runtime session for the process. It applies the configured pooling (CLS for the BGE family) and L2 normalization, and prepends `Embedding:QueryInstruction` to query text only. The model is loaded eagerly at startup (`InitializeOmniSift`), so a missing model fails the boot. `EmbeddingOptionsValidator` refuses `Provider=OpenAi` unless `OpenAI:EmbeddingDimensions` equals `Embedding:Dimensions` (the column width) and an OpenAI key is set.

## 5. Semantic Kernel Agent Architecture

### 5.1 Agent Configuration

- **Chat completion:** `AddOpenAIChatCompletion(Anthropic:ModelId, Anthropic:ApiKey)` on a named `HttpClient` whose base address is `https://api.anthropic.com/v1/` (standard resilience handler, 3 retries, 120 s total). With no key the kernel has no chat service and the agent and entity endpoints answer `503`.
- **Function calling:** `FunctionChoiceBehavior.Auto()`.
- **Research profile:** `ResearchProfileOptions` (section `ResearchProfile`) supplies the system prompt, the plugins imported into each request's kernel and the report-title template (`{profile}`, `{date}`). Unset values fall back to `ResearchProfiles.FamilyHistory`. `ResearchProfileOptionsValidator` rejects unknown or duplicate plugin names at startup.
- **History:** `ChatHistoryFactory` builds system prompt + the last `Agent:MaxHistoryMessages` (default 20) `user`/`assistant` messages + the query.

### 5.2 Plugin Specifications

Imported under the names in `AgentPlugins`; those names are what `pluginsUsed` reports.

#### VectorSearch (`VectorSearchPlugin`)
```
Function: SearchDocuments(query: string, topK: int = 5)
→ IHybridSearchService.SearchAsync(query, topK)
→ Returns the best chunks with file name, source type and relevance score
→ Adds document citations to the request's ICitationAccumulator
```

`HybridSearchService` (`Services/Search/`):
```
1. Embed the query once
2. Vector arm: tenant's chunks nearest by cosine distance, topK × 3 candidates,
   drop similarity < 0.25
3. Keyword arm: tenant's chunks by pg_trgm word_similarity(query, content) ≥ 0.3,
   topK × 3 candidates, searched across all chunks (not only vector hits)
4. Fuse the two rankings with Reciprocal Rank Fusion (k = 60), take topK
5. Relevance shown = cosine similarity when known, else keyword similarity
```

#### WebScraper (`WebScraperPlugin`)
```
Function: SearchWeb(query: string, maxResults: int = 5, clamped to 1..10)
→ Tavily search API (https://api.tavily.com/, Authorization: Bearer <Tavily:ApiKey>)
→ Returns titles, URLs and snippets; adds web citations
→ Without a key: returns "web search is not available" to the model
```

#### WaybackMachine (`WaybackMachinePlugin`)
```
Function: GetArchivedPage(url: string)
→ https://archive.org/wayback/available?url={url}
→ Returns the closest archived snapshot URL and timestamp; adds an archive citation
```

Search query text is not logged by the plugins.

### 5.3 Agent Execution Flow

```
1. Client posts AgentQueryRequest to /api/agent/query or /api/agent/query/stream
2. ResearchAgentService resolves the chat service (missing → 503 before any SSE header)
3. ChatHistoryFactory builds the history; SK runs with auto function calling
4. PluginInvocationTrackingFilter records each plugin call in IInvocationTracker;
   plugins add citations to ICitationAccumulator
5. Buffered: one response. Streamed: AgentSseWriter writes
     data: {"type":"delta","content":"..."}
     data: {"type":"final","pluginsUsed":[...],"durationMs":n,"sources":[...]}
     data: {"type":"error","message":"..."}   (only if the stream fails after it started)
6. IQueryHistoryRecorder saves query_history (with the citations) and an audit entry,
   even if the client disconnected
```

### 5.4 Reports

`ReportRequestFactory` maps a `GenerateReportRequest` (title, conversation turns with citations) onto a `ReportDocument`, stamped with `TimeProvider`'s UTC time; a blank title uses the profile's `ReportTitle`. `IReportRenderer<string>` (`MarkdownReportRenderer`) and `IReportRenderer<byte[]>` (`PdfReportRenderer`, QuestPDF) render the same document.

## 6. API Design

### 6.1 Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/register` | HS256 IdP: create tenant + owner, return a token (anonymous; removed when Supabase is configured) |
| `POST` | `/api/auth/login` | HS256 IdP: email + password + tenant id → token (anonymous; removed when Supabase is configured) |
| `POST` | `/api/datasources/upload` | Upload a file (`file`, optional `sourceType`) for async ingestion → `202` |
| `POST` | `/api/datasources/web` | Ingest a web page → `200` with status `completed` or `failed` |
| `GET` | `/api/datasources?skip=&take=` | List the tenant's data sources, newest first (take default 200, max 500) |
| `GET` | `/api/datasources/{id}` | Data source details (poll target after upload) |
| `DELETE` | `/api/datasources/{id}` | Delete a data source and its chunks → `204` |
| `POST` | `/api/agent/query` | Research query, buffered |
| `POST` | `/api/agent/query/stream` | Research query, Server-Sent Events |
| `POST` | `/api/agent/report` | Markdown report of a conversation |
| `POST` | `/api/agent/report/pdf` | PDF report of a conversation |
| `POST` | `/api/entities/extract` | Entity graph + timeline from the tenant's chunks (LLM, up to 16,000 characters of corpus) |
| `GET` | `/api/account/export` | Export data sources, query history and audit log |
| `DELETE` | `/api/account/data` | Erase the tenant's data (`{"confirm":"DELETE"}`) → `204` |
| `GET` | `/api/audit` | Latest 100 audit entries |
| `GET` | `/health` | `200 Healthy` / `503 Unhealthy`, includes database reachability (anonymous) |

Every controller endpoint requires the default policy (authenticated + `tenant_id` claim) unless `[AllowAnonymous]`. The agent, data source and entity controllers are rate-limited per tenant (token bucket; defaults 20 burst, 10 per minute; `429` when exhausted).

### 6.2 Request/Response Contracts

No tenant header: requests carry `Authorization: Bearer <jwt>` and the tenant comes from its `tenant_id` claim. JSON is camelCase (source-generated `OmniSiftJsonContext`).

**Upload Request:** `multipart/form-data` with `file` and optional `sourceType`

**Agent Query Request** (`query` 3–5,000 characters; `conversationHistory` ≤ 50 messages, each ≤ 32,000 characters, role `user` or `assistant`):
```json
{
  "query": "What addresses did John Smith live at?",
  "conversationHistory": [
    { "role": "user", "content": "..." },
    { "role": "assistant", "content": "..." }
  ]
}
```

**Agent Query Response:**
```json
{
  "response": "Based on the documents...",
  "sources": [
    {
      "type": "document",
      "dataSourceId": "guid",
      "chunkId": "guid",
      "title": "letters.pdf",
      "relevanceScore": 0.92,
      "snippet": "..."
    }
  ],
  "pluginsUsed": ["VectorSearch", "WaybackMachine"],
  "durationMs": 4210
}
```

**Errors:** RFC 7807 ProblemDetails (`application/problem+json`) for every error, including `401`/`403`. `GlobalExceptionHandler` maps `OmniSiftException` subclasses to their status and message (e.g. `UnsupportedSourceTypeException` → 400, `BlockedUrlException` → 400, `TenantContextMissingException` → 403, `LlmNotConfiguredException` → 503), `BadHttpRequestException` to its status, `HttpRequestException` to a generic 502 and anything else to a generic 500. Every body carries a `traceId`.

## 7. Security Architecture

### 7.1 Defense Layers

| Layer | Mechanism |
|---|---|
| **Transport** | HTTPS/TLS at the reverse proxy in production |
| **Authentication** | Supabase ES256/RS256 via OIDC discovery (`Supabase:Url`), no symmetric secret. Fallback: in-house HS256 IdP (BCrypt passwords), Development/Testing only unless `Jwt:AllowLocalIdentityProvider=true` with a unique ≥ 32-byte secret |
| **Authorization** | Default policy: authenticated + `tenant_id` claim on every controller; `[AllowAnonymous]` opts out |
| **Data Isolation** | EF global query filters (fail closed) + RLS via `app.current_tenant` set per connection open |
| **Abuse limits** | Per-tenant token-bucket rate limiting after authentication; request size and field-length limits |
| **Outbound requests** | Web ingestion SSRF guard: http(s) only, public addresses only (loopback, private, CGNAT, link-local incl. metadata, unique-local, multicast and reserved blocked), checked per connection and redirect, no proxy |
| **Errors** | ProblemDetails; exception messages never returned except for `OmniSiftException` |
| **Audit** | `audit_log` with acting user, append-only under RLS |
| **Secret Management** | Environment variables in dev; Docker file secrets (`/run/secrets`, `AddKeyPerFile`) in prod |

### 7.2 File Upload Security

- Maximum file size: 50 MB (`RequestSizeLimit` + explicit check); file name ≤ 512 characters; `sourceType` ≤ 50 characters
- Source type must resolve through the extractor registry, else `400` before anything is stored
- Files are buffered in memory for the worker, never written to disk
- Only extracted text chunks and embeddings are persisted
- PII (by `PiiScanner`) is flagged on the data source, not rejected

### 7.3 Configuration validation

Options are bound with data-annotation validation and `ValidateOnStart`; `InitializeOmniSift` runs the startup validator before migrating. Custom validators: `JwtOptionsValidator` (auth mode), `EmbeddingOptionsValidator` (provider/column width), `ResearchProfileOptionsValidator` (plugin names), `ChunkingOptions` (overlap < max).

## 8. Deployment Architecture

### 8.1 Docker Compose Services

| Service | Image | Port (dev host) | Dependencies |
|---|---|---|---|
| `db` | `pgvector/pgvector:pg16` + `infra/db/init.sql` | 5432 | — |
| `api` | Custom, multi-stage .NET 10 (model-fetch, build, `aspnet:10.0` runtime, non-root) | 5000 → 8080 | db (healthy) |
| `web` | Custom, .NET 10 SDK build + `nginx:alpine` runtime | 5080 → 80 | api |

Production uses the `docker-compose.prod.yml` overlay with GHCR images built by the manually dispatched `deploy.yml`. See [DEPLOY.md](./DEPLOY.md).

### 8.2 Multi-Stage Docker Builds

1. **API:** a `debian:bookworm-slim` stage downloads `model.onnx` + `vocab.txt` pinned to Hugging Face commit SHAs and verified with `sha256sum`; the SDK stage restores and publishes; the runtime stage copies the app and the model to `/app/models/bge-small-en-v1.5/`.
2. **Web:** the SDK stage publishes the Blazor app; nginx serves it under `/app/` and the landing page at `/`. `docker-entrypoint.d/40-render-appsettings.sh` writes `app/appsettings.json` from `API_BASE_URL` at container start (empty = the client calls `api/...` on its own origin).

### 8.3 Web Client

- `OmniSiftApiClient` is a typed `HttpClient` whose base address comes from `ApiBaseAddress.Resolve(ApiBaseUrl, host origin)`; every call returns `ApiResult`, with `ApiProblemReader` turning ProblemDetails into user messages.
- `AuthHandler` adds `Authorization: Bearer` from `AuthStateService` (JWT in `localStorage`, expiry from the `exp` claim). No tenant header is sent.
- `AuthRouteView` + `PageAccess` send anonymous users to the login page for pages that require authentication; sign-out, a 401 or token expiry re-evaluates the route.
- `DataSourcesViewModel` pages the list and polls upload status after a `202`; `AgentChatViewModel` streams answers through `SseParser`, supports Stop, surfaces `error` frames and does not re-run a failed query.

## 9. Testing Strategy

| Level | Framework | Scope |
|---|---|---|
| **Unit Tests** | xUnit + Moq + FluentAssertions | API services, options, data layer, plugins, Web client services and view models |
| **Integration Tests** | xUnit + `WebApplicationFactory`, EF Core InMemory, fake chat completion | HTTP endpoints, auth modes, tenant isolation, error shapes, rate limiting |

CI runs the full unit and integration suites on every pull request and push to `main`.

### 9.1 Unit Test Coverage (selected classes)

- Ingestion: `IngestionPipelineTests`, `TextExtractorRegistryTests`, `TextChunkerTests`, `SmsTextExtractorTests`, `WebTextExtractorTests`, `DocxTextExtractorTests`, `EmailTextExtractorTests`, `WebPageFetcherTests`, `OutboundAddressFilterTests`, `PiiScannerTests`
- Embeddings and search: `OnnxEmbeddingServiceTests`, `OnnxEmbeddingParityTests`, `HybridSearchServiceTests`, `ReciprocalRankFusionTests`, `VectorSearchPluginTests`, `WaybackMachinePluginTests`
- Agent and reports: `ResearchAgentServiceTests`, `ChatHistoryFactoryTests`, `AgentSseWriterTests`, `CitationAccumulatorTests`, `ReportRequestFactoryTests`, `MarkdownReportRendererTests`, `PdfReportRendererTests`
- Data and tenancy: `TenantQueryFilterTests`, `TenantConnectionInterceptorTests`, `AuditLogRowLevelSecurityMigrationTests`, `DataSourceProjectionsTests`, `TimestampInterceptorTests`, `TenantMiddlewareTests`
- Configuration: `JwtOptionsValidatorTests`, `EmbeddingOptionsValidatorTests`, `ResearchProfileOptionsTests`, `CorsOptionsTests`
- Deployment: `DockerModelPackagingTests`
- Web client: `OmniSiftApiClientTests`, `AuthHandlerTests`, `AuthStateServiceTests`, `SseParserTests`, `AgentChatViewModelTests`, `DataSourcesViewModelTests`, `ApiBaseAddressTests`, `PageAccessTests`

### 9.2 Integration Test Coverage

- `DataSourcesEndpointTests` — list paging, upload type resolution, web ingestion SSRF rejection, 401/404 paths
- `AgentEndpointTests`, `ReportEndpointTests` — agent query and streaming with a fake LLM, reports
- `AuthenticationTests` — 401/403 paths, tenant isolation from the claim, spoofed tenant header ignored
- `AuthModeTests` — `/api/auth` served under HS256, absent when Supabase is configured
- `AccountEndpointTests` — erase (with its audit entry) and the confirmation guard
- `ErrorHandlingTests` — ProblemDetails shapes
- `RateLimitingTests` — per-tenant buckets
- `HealthEndpointTests` — `/health`
- `JsonSerializationTests` — source-generated JSON contract
