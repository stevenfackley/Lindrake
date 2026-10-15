# Lindrake <sub>`>o)`</sub>

![.NET](https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white) ![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white) ![CSS3](https://img.shields.io/badge/css3-%231572B6.svg?style=for-the-badge&logo=css3&logoColor=white) ![Docker](https://img.shields.io/badge/docker-%232496ED.svg?style=for-the-badge&logo=docker&logoColor=white) ![HTML5](https://img.shields.io/badge/html5-%23E34F26.svg?style=for-the-badge&logo=html5&logoColor=white) ![PostgreSQL](https://img.shields.io/badge/PostgreSQL-316192?style=for-the-badge&logo=postgresql&logoColor=white)




[![CI](https://github.com/stevenfackley/Lindrake/actions/workflows/ci.yml/badge.svg)](https://github.com/stevenfackley/Lindrake/actions/workflows/ci.yml)

**AI-Powered Multi-Tenant Research SaaS for Personal & Family History**

Lindrake is an agentic research application that aggregates, parses, and synthesizes personal and family history data. It searches internal user-provided documents, live web search results and web archives to build comprehensive personal profiles and timelines.

Lindrake was called OmniSift until 2026-10-14 ([why](DECISIONS.md#2026-10-14--renamed-omnisift-to-lindrake)).

---

## 🏗️ Architecture

| Layer | Technology |
|---|---|
| **Frontend** | Blazor WebAssembly (.NET 10), served by nginx under `/app/` |
| **Backend API** | ASP.NET Core, .NET 10 (C# 14, `LangVersion` preview) |
| **AI Orchestration** | Microsoft Semantic Kernel (automatic function calling) |
| **Database** | PostgreSQL + pgvector + pg_trgm (EF Core 10 / Npgsql); deployments on qavren-db (shared Supabase Postgres, schema `lindrake`), local compose `db` for development |
| **Containerization** | Docker & Docker Compose |
| **LLM** | Anthropic Claude: `Anthropic:ModelId`, default `claude-sonnet-4-20250514`, called through Semantic Kernel's OpenAI connector against Anthropic's OpenAI-compatible endpoint |
| **Embeddings** | Local ONNX `bge-small-en-v1.5` (384 dimensions, 512-token window). OpenAI `text-embedding-3-large` remains selectable, see [Embeddings](#embeddings) |
| **Web search / archives** | Tavily search API, Internet Archive Wayback Machine |
| **Auth** | Supabase-issued JWTs (ES256/RS256 via OIDC discovery) in production; in-house HS256 identity provider for local development |

## 🚀 Quick Start

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) & [Docker Compose](https://docs.docker.com/compose/install/)
- (Optional) [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for local development and test execution

### 1. Clone & Configure

```bash
git clone https://github.com/stevenfackley/Lindrake.git
cd Lindrake
```

Optionally fill in your API keys in `.env.dev` (committed with safe defaults; only the keys need updating):

```bash
# Edit .env.dev and set your personal dev keys
ANTHROPIC_API_KEY=sk-ant-...   # agent + entity graph; without it those endpoints answer 503
TAVILY_API_KEY=tvly-...        # web search; without it the agent is told web search is unavailable
OPENAI_API_KEY=sk-...          # only needed with Embedding:Provider=OpenAi
```

### 2. Build & Run

```bash
docker compose up --build
```

`docker-compose.yml` defaults every value it interpolates (database name and passwords, JWT secret, API keys) to its `.env.dev` value, so this runs as is, with no API keys. Compose interpolation does not read `.env.dev` by itself: to apply your edits to it (your API keys), add `--env-file .env.dev` to compose commands, e.g. `docker compose --env-file .env.dev up --build`.

The API image downloads the ONNX embedding model (about 127 MB, pinned and checksummed) during the build. On first start of an empty volume, `db` runs `infra/db/init.sql` (extensions, `lindrake_app` role); the API then applies EF Core migrations and loads the model. A missing model is a startup failure, not a degraded mode.

### 3. Access

| Service | URL |
|---|---|
| **Landing page** | [http://localhost:5080](http://localhost:5080) |
| **App (Blazor)** | [http://localhost:5080/app/](http://localhost:5080/app/) |
| **Backend API** | [http://localhost:5000](http://localhost:5000) |
| **API Docs (Swagger, Development only)** | [http://localhost:5000/swagger](http://localhost:5000/swagger) |
| **Health** | [http://localhost:5000/health](http://localhost:5000/health) |
| **PostgreSQL** | `localhost:5432` |

Locally there is no Supabase project, so the API runs the in-house HS256 identity provider: create an account on the app's Register page (`/api/auth/register` creates a tenant and its owner) and sign in.

---

## 📂 Project Structure

```
Lindrake/
├── Lindrake.sln                       # Solution file
├── docker-compose.yml                 # Dev stack: db + api + web (defaults = .env.dev values)
├── docker-compose.prod.yml            # Prod overlay: GHCR images, Docker secrets, healthchecks, no db (qavren-db)
├── .env.example                       # Environment variable reference
├── .env.dev                           # Development defaults (committed)
├── .env.test                          # Test defaults (committed)
├── .env.prod                          # Production values (gitignored)
├── .github/
│   ├── dependabot.yml
│   └── workflows/
│       ├── ci.yml                     # Build, test, audit (shared ci-dotnet workflow)
│       ├── deploy.yml                 # Manual: build + push GHCR images, SSH deploy
│       └── dependabot-auto-merge.yml
├── docs/                              # PDD, SDD, deploy guide, schema provisioning, roadmap
├── infra/db/init.sql                  # Local db superuser setup: extensions, lindrake_app role, grants
├── landing/                           # Static marketing site served at /
├── scripts/                           # Test runner, embedding-model fetch, secret scan
├── secrets/README.md                  # How to populate Docker secret files
├── src/
│   ├── Lindrake.Shared/               # DTOs, contracts, source-generated JSON context
│   ├── Lindrake.Api/                  # ASP.NET Core Web API
│   │   ├── Controllers/               # Thin HTTP adapters
│   │   ├── Data/                      # DbContext, migrations, interceptors, Projections/
│   │   ├── Extensions/                # Composition root: service modules + pipeline
│   │   ├── Infrastructure/            # Exception handler, domain exceptions, auth gate
│   │   ├── Middleware/                # Correlation ID, tenant context
│   │   ├── Models/                    # Domain entities
│   │   ├── Options/                   # Strongly-typed, startup-validated options
│   │   ├── Plugins/                   # Semantic Kernel plugins
│   │   ├── Services/                  # Ingestion/, Extraction/, Search/, Agent/, Reports/
│   │   └── Dockerfile
│   └── Lindrake.Web/                  # Blazor WebAssembly frontend
│       ├── Pages/                     # Routable pages
│       ├── Components/                # Route guard, alerts, async status
│       ├── ViewModels/                # Page state and behaviour (unit-tested)
│       ├── Layout/                    # App shell layout
│       ├── Services/                  # Typed API client, auth state, SSE parser
│       ├── docker-entrypoint.d/       # Renders appsettings.json from API_BASE_URL at start
│       └── Dockerfile
└── tests/
    ├── Lindrake.UnitTests/            # xUnit + Moq + FluentAssertions (API and Web)
    └── Lindrake.IntegrationTests/     # WebApplicationFactory + EF Core InMemory; Postgres/ real-Postgres suite
```

---

## 🧠 AI Agent Capabilities

Lindrake uses **Microsoft Semantic Kernel** to orchestrate a research agent that decides which plugins to call:

| Plugin | Function | Description |
|---|---|---|
| **VectorSearch** | `SearchDocuments` | Hybrid search over the tenant's uploaded and embedded documents |
| **WebScraper** | `SearchWeb` | Live web search through the Tavily API |
| **WaybackMachine** | `GetArchivedPage` | Closest archived snapshot of a URL from the Internet Archive |

The agent's persona is configuration, not code. The `ResearchProfile` section sets:

| Key | Meaning |
|---|---|
| `Name` | Profile name, used in logs and the `{profile}` report-title token |
| `SystemPrompt` | System prompt sent at the start of every conversation |
| `EnabledPlugins` | Plugins imported into the kernel (`VectorSearch`, `WebScraper`, `WaybackMachine`); absent means all. Unknown or duplicate names fail startup |
| `ReportTitle` | Default report title; supports `{profile}` and `{date}` (UTC, `yyyy-MM-dd`) |

Unset keys fall back to the built-in family-history profile.

## 📄 Data Ingestion

Supported upload formats. The source type comes from the optional `sourceType` form field, else from the MIME type, else (missing or `application/octet-stream` MIME type) from the file extension:

- **PDF** (`.pdf`): text extracted with PdfPig
- **SMS exports** (`.csv`, `.json`): CSV or JSON message lists normalized to `sender (timestamp) message` lines
- **HTML pages** (`.html`, `.htm`): article text isolated with HtmlAgilityPack
- **Word documents** (`.docx`): extracted with DocumentFormat.OpenXml (legacy binary `.doc` is rejected)
- **Email** (`.eml`, `.mbox`): parsed with MimeKit (`.msg` is rejected)
- **Plain text** (`.txt`, `.md`)

Uploads are capped at 50 MB, validated before anything is stored, and answered with `202 Accepted`; a background worker ingests them and the client polls `GET /api/datasources/{id}` until the status is `completed` or `failed`. An unknown `sourceType` or file type is a `400`.

Web pages are ingested synchronously through `POST /api/datasources/web`. The fetch is SSRF-guarded (only public addresses, checked on every connection and redirect hop), capped at 10 MB (`Ingestion:MaxWebPageBytes`) and 30 seconds. A blocked URL is a `400`, an upstream failure a `502`; a page that downloads but fails ingestion is a `200` whose `status` is `failed`.

Every source goes through one pipeline: extract, PII scan (flags only, never rejects), chunk, embed, persist. Chunks are sized in tokens of the active embedding model (480 tokens with 48 tokens of overlap by default, `Chunking` section) so every chunk fits the model's 512-token window.

### Embeddings

The default provider is the local ONNX model (`Embedding:Provider=Onnx`), so embedding has no per-token cost. Switching to OpenAI (`Embedding:Provider=OpenAi` plus `OpenAI:ApiKey`) is not a config flip: `document_chunks.embedding` is `vector(384)` and `text-embedding-3-large` emits 3072 dimensions, so startup refuses the mismatch until a migration changes the column.

## 🔎 Search

`SearchDocuments` runs a hybrid search over the tenant's chunks: a vector arm (pgvector cosine similarity, minimum 0.25) and an independent keyword arm (pg_trgm `word_similarity`, minimum 0.3) are fused with Reciprocal Rank Fusion (k = 60). The keyword arm searches all of the tenant's chunks, so exact names the embedding misses still surface.

## 🔐 Multi-Tenancy & Auth

- Tenant identity comes only from the validated JWT's `tenant_id` claim. There is no tenant header; `X-Tenant-Id` is ignored.
- **Production:** set `Supabase:Url`; the API validates Supabase-issued ES256/RS256 tokens via OIDC discovery (audience `authenticated`), and the Supabase project must emit a `tenant_id` custom claim. `/api/auth/*` does not exist in this mode.
- **Development / Testing:** with `Supabase:Url` empty, the in-house HS256 identity provider (`/api/auth/register`, `/api/auth/login`) issues tokens signed with `Jwt:Secret` (at least 32 bytes). Outside Development/Testing it starts only with `Jwt:AllowLocalIdentityProvider=true` and a unique secret; otherwise the API refuses to start.
- Every controller requires an authenticated identity with a `tenant_id` claim unless it is marked `[AllowAnonymous]`.
- Isolation is enforced twice: an EF Core global query filter on every tenant-owned entity (an unbound scope matches no rows) and PostgreSQL Row-Level Security keyed on `app.current_tenant`, which an EF connection interceptor sets each time a connection opens. The app connects as a non-superuser role without BYPASSRLS (`lindrake_app` locally, `lindrake` on qavren-db) so RLS applies.

## 🌐 API Conventions

- **Errors:** every error is an RFC 7807 ProblemDetails body (`application/problem+json`), including `401`/`403` from authentication and authorization. Unexpected exceptions are a generic `500` with a `traceId`; their messages are logged, never returned.
- **Health:** `GET /health` (anonymous) answers `200 Healthy` or `503 Unhealthy` and includes a database check. There is no `/api/health`.
- **Paging:** `GET /api/datasources?skip=0&take=200` returns newest first; `take` defaults to 200, maximum 500.
- **Agent requests:** `query` 3 to 5,000 characters; `conversationHistory` at most 50 messages of at most 32,000 characters each, roles `user` or `assistant`. The server forwards only the most recent `Agent:MaxHistoryMessages` (default 20) to the LLM.
- **Streaming:** `POST /api/agent/query/stream` returns Server-Sent Events: `delta` frames with text, then one `final` frame (`pluginsUsed`, `durationMs`, `sources`); a failure after the stream started ends it with an `error` frame.
- **Rate limiting:** the agent, data source and entity endpoints share a per-tenant token bucket (default burst 20, 10 tokens per minute, `RateLimiting` section), partitioned on the authenticated `tenant_id`; excess requests get `429`.

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/register`, `/api/auth/login` | HS256 identity provider (absent when Supabase is configured) |
| `POST` | `/api/datasources/upload` | Upload a file for asynchronous ingestion (`202`) |
| `POST` | `/api/datasources/web` | Ingest a web page by URL |
| `GET` | `/api/datasources` | List data sources (paged) |
| `GET` / `DELETE` | `/api/datasources/{id}` | Get, or delete with its chunks |
| `POST` | `/api/agent/query`, `/api/agent/query/stream` | Research query (buffered or SSE) |
| `POST` | `/api/agent/report`, `/api/agent/report/pdf` | Cited report as Markdown or PDF |
| `POST` | `/api/entities/extract` | Entity graph and timeline over the tenant's documents |
| `GET` | `/api/account/export` | Export the tenant's data |
| `DELETE` | `/api/account/data` | Erase the tenant's data (body `{"confirm":"DELETE"}`) |
| `GET` | `/api/audit` | Latest 100 audit entries |
| `GET` | `/health` | Liveness + database check |

## 🧪 Testing

```bash
# Run the reproducible repo-owned test path
pwsh ./scripts/run-tests.ps1

# Or run the projects directly
dotnet test tests/Lindrake.UnitTests/Lindrake.UnitTests.csproj -f net10.0
dotnet test tests/Lindrake.IntegrationTests/Lindrake.IntegrationTests.csproj -f net10.0
```

Integration tests run the API in the `Testing` environment with a fake chat-completion service and no API keys. Most run on EF Core InMemory; the Postgres suite (`tests/Lindrake.IntegrationTests/Postgres`: RLS, the tenant interceptor, hybrid search, cascades, migrations in the local and qavren-db layouts) runs when `ConnectionStrings__PostgreSQL` names a superuser connection to a pgvector server, and is skipped otherwise. Against the compose `db`:

```bash
docker compose up -d db
ConnectionStrings__PostgreSQL="Host=localhost;Port=5432;Database=lindrake;Username=lindrake;Password=lindrake_dev" \
  dotnet test tests/Lindrake.IntegrationTests --filter "FullyQualifiedName~Lindrake.IntegrationTests.Postgres"
```

CI runs the full unit and integration suites, the Postgres suite included (a `pgvector/pgvector:pg16` service), on every pull request and push to `main`.

## 📝 Documentation

- [Product Design Document (PDD)](./docs/PDD.md)
- [Software Design Document (SDD)](./docs/SDD.md)
- [Deploy Guide](./docs/DEPLOY.md)
- [Schema Provisioning](./docs/schema-provisioning.md)
- [Marketability Roadmap](./docs/marketability-roadmap.md)
- [Architecture Decisions](./DECISIONS.md)
- [Changelog](./CHANGELOG.md)

## 📜 License

Source-available — forks permitted for personal and non-commercial use. Commercial use requires written permission. See [LICENSE](./LICENSE) for full terms.
