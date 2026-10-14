# OmniSift — Software Design Document (SDD)

**Version:** 1.0
**Date:** 2026-02-25
**Status:** Active Development

---

## 1. System Overview

OmniSift is a containerized, multi-tenant SaaS application built on a modern .NET 8 stack. The system follows a clean separation between a Blazor WebAssembly frontend and an ASP.NET Core Web API backend, with AI orchestration handled by Microsoft Semantic Kernel.

### High-Level Architecture

```
┌──────────────────┐     HTTP/JSON     ┌──────────────────────────────┐
│   Blazor WASM    │ ◄──────────────► │    ASP.NET Core Web API       │
│   (Frontend)     │   + X-Tenant-Id   │    (Backend)                  │
│   Port: 5080     │                   │    Port: 5000                 │
└──────────────────┘                   │                               │
                                       │  ┌─────────────────────────┐  │
                                       │  │   Semantic Kernel       │  │
                                       │  │   ┌─────────────────┐   │  │
                                       │  │   │ VectorSearch    │   │  │
                                       │  │   │ WebScraper      │   │  │
                                       │  │   │ WaybackMachine  │   │  │
                                       │  │   └─────────────────┘   │  │
                                       │  └─────────────────────────┘  │
                                       │                               │
                                       │  ┌─────────────────────────┐  │
                                       │  │ Data Ingestion Pipeline │  │
                                       │  │ PDF │ SMS │ Web HTML    │  │
                                       │  └─────────────────────────┘  │
                                       └──────────────┬───────────────┘
                                                      │
                                              ┌───────▼────────┐
                                              │  PostgreSQL     │
                                              │  + pgvector     │
                                              │  Port: 5432     │
                                              │  (RLS Enabled)  │
                                              └────────────────┘
```

## 2. Technology Stack

| Component | Technology | Version |
|---|---|---|
| Runtime | .NET | 8.0+ |
| Language | C# | 12 |
| Frontend | Blazor WebAssembly | .NET 8 |
| Backend | ASP.NET Core Web API | .NET 8 |
| AI Orchestration | Microsoft Semantic Kernel | 1.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16 |
| Vector Extension | pgvector | 0.7+ |
| PDF Parsing | PdfPig | Latest |
| HTML Parsing | HtmlAgilityPack | Latest |
| LLM Provider | Anthropic Claude 4.6 Sonnet | API |
| Embeddings | OpenAI text-embedding-3-large | API |
| Containerization | Docker + Docker Compose | Latest |

## 3. Multi-Tenancy Architecture

### 3.1 Strategy: Shared Database, Shared Schema with RLS

OmniSift uses a **shared database, shared schema** multi-tenancy model with PostgreSQL Row-Level Security (RLS) as the enforcement mechanism.

**Why this approach:**
- Cost-efficient — single database instance serves all tenants
- Simplified operations — one schema to migrate and maintain
- Strong isolation — RLS enforced at the database engine level (not application level)
- Scalable — can shard later if needed

### 3.2 Tenant Resolution Flow

```
1. HTTP Request arrives with header: X-Tenant-Id: {guid}
2. TenantMiddleware extracts and validates the tenant ID
3. Middleware sets PostgreSQL session variable: SET app.current_tenant = '{guid}'
4. All subsequent queries are automatically filtered by RLS policies
5. Response returned to client
```

### 3.3 Row-Level Security Policies

Every tenant-scoped table has the following RLS policy:

```sql
ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON {table}
    USING (tenant_id = current_setting('app.current_tenant')::uuid);
```

### 3.4 Database Schema

```
tenants
├── id (UUID, PK)
├── name (VARCHAR)
├── created_at (TIMESTAMPTZ)
└── is_active (BOOLEAN)

data_sources
├── id (UUID, PK)
├── tenant_id (UUID, FK → tenants)
├── source_type (VARCHAR: pdf, sms, web)
├── file_name (VARCHAR)
├── original_url (VARCHAR, nullable)
├── status (VARCHAR: pending, processing, completed, failed)
├── metadata (JSONB)
├── created_at (TIMESTAMPTZ)
└── updated_at (TIMESTAMPTZ)

document_chunks
├── id (UUID, PK)
├── tenant_id (UUID, FK → tenants)
├── data_source_id (UUID, FK → data_sources)
├── content (TEXT)
├── chunk_index (INTEGER)
├── token_count (INTEGER)
├── embedding (VECTOR(3072))  -- text-embedding-3-large dimension
├── metadata (JSONB)
├── created_at (TIMESTAMPTZ)
└── updated_at (TIMESTAMPTZ)
```

## 4. Data Ingestion Pipeline

### 4.1 Pipeline Architecture

```
Upload → Validate → Extract Text → Chunk → Embed → Store
```

Each stage is implemented as a discrete service following the Strategy pattern:

| Stage | Responsibility |
|---|---|
| **ITextExtractor** | Extract raw text from source format (PDF, CSV, HTML) |
| **ITextChunker** | Split text into overlapping chunks (500 tokens, 50 overlap) |
| **IEmbeddingService** | Generate vector embeddings via OpenAI API |
| **IDocumentIngestionService** | Orchestrate the full pipeline |

### 4.2 Text Extraction Strategies

| Source Type | Library | Extraction Strategy |
|---|---|---|
| **PDF** | PdfPig | Extract text page-by-page, concatenate with page markers |
| **SMS (CSV)** | System.Text.Json / CsvHelper logic | Parse rows; concatenate sender + timestamp + message |
| **SMS (JSON)** | System.Text.Json | Deserialize message array; format as conversation |
| **Web HTML** | HtmlAgilityPack | Strip scripts/styles; extract `<article>`, `<main>`, or `<body>` text |

### 4.3 Chunking Algorithm

```
Input: Raw text string, chunk_size=500 tokens, overlap=50 tokens
Output: List<TextChunk>

1. Tokenize text (approximate: split on whitespace)
2. Sliding window of chunk_size tokens
3. Advance window by (chunk_size - overlap) tokens
4. Each chunk gets sequential index and token count metadata
```

## 5. Semantic Kernel Agent Architecture

### 5.1 Agent Configuration

The Semantic Kernel agent is configured with:
- **Chat Completion Service**: Anthropic Claude 4.6 Sonnet
- **Function Calling**: Automatic (agent decides which plugins to invoke)
- **System Prompt**: Research-focused persona with instructions to cite sources

### 5.2 Plugin Specifications

#### VectorSearchPlugin
```
Function: SearchDocuments(query: string, topK: int = 5)
→ Embeds the query using OpenAI
→ Performs cosine similarity search against document_chunks
→ Returns top-K matching chunks with metadata
```

#### WebScraperPlugin
```
Function: SearchWeb(query: string, maxResults: int = 5)
→ Calls external search API (Tavily/Bing) with query
→ Returns titles, URLs, and snippets
```

#### WaybackMachinePlugin
```
Function: GetArchivedPage(url: string)
→ Calls http://archive.org/wayback/available?url={url}
→ Returns closest archived snapshot URL and timestamp
→ Returns null if no snapshot available
```

### 5.3 Agent Execution Flow

```
1. User submits query via chat interface
2. API creates Semantic Kernel ChatHistory with system prompt
3. Agent processes query with auto function calling enabled
4. Agent may invoke 0-N plugins based on query analysis
5. Agent synthesizes all plugin results into a coherent response
6. Response streamed back to client with source citations
```

## 6. API Design

### 6.1 Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/datasources/upload` | Upload a file for ingestion |
| `GET` | `/api/datasources` | List all data sources for tenant |
| `GET` | `/api/datasources/{id}` | Get data source details |
| `DELETE` | `/api/datasources/{id}` | Delete a data source and its chunks |
| `POST` | `/api/agent/query` | Submit a research query to the agent |
| `GET` | `/health` | Health check endpoint (200 Healthy / 503 Unhealthy, includes database reachability) |

### 6.2 Request/Response Contracts

All requests must include the `X-Tenant-Id` header.

**Upload Request:** `multipart/form-data` with file and source type

**Agent Query Request:**
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
      "relevanceScore": 0.92
    }
  ],
  "pluginsUsed": ["VectorSearchPlugin", "WaybackMachinePlugin"]
}
```

## 7. Security Architecture

### 7.1 Defense Layers

| Layer | Mechanism |
|---|---|
| **Transport** | HTTPS/TLS (reverse proxy in production) |
| **Authentication** | JWT Bearer tokens (placeholder; extensible to OAuth2) |
| **Authorization** | Tenant-scoped; middleware enforces tenant context |
| **Data Isolation** | PostgreSQL RLS policies per tenant |
| **Input Validation** | Model validation + file type/size restrictions |
| **Secret Management** | All secrets via environment variables |

### 7.2 File Upload Security

- Maximum file size: 50MB
- Allowed MIME types: `application/pdf`, `text/csv`, `application/json`, `text/html`
- Files are processed in-memory (not stored on disk)
- Content is extracted, chunked, and only text + embeddings are persisted

## 8. Deployment Architecture

### 8.1 Docker Compose Services

| Service | Image | Port | Dependencies |
|---|---|---|---|
| `db` | postgres:16 + pgvector | 5432 | — |
| `api` | Custom (multi-stage .NET 8) | 5000 | db |
| `web` | Custom (nginx + Blazor WASM) | 5080 | api |

### 8.2 Multi-Stage Docker Builds

Both API and Web projects use multi-stage builds:
1. **Build stage**: .NET SDK image, restore + publish
2. **Runtime stage**: .NET ASP.NET runtime (API) or nginx (Web)

## 9. Testing Strategy

| Level | Framework | Scope |
|---|---|---|
| **Unit Tests** | xUnit + Moq | Text extraction, chunking, plugin logic |
| **Integration Tests** | xUnit + WebApplicationFactory | API endpoints, DB operations, agent routing |

### 9.1 Unit Test Coverage

- `TextChunkerTests` — Verify chunk sizes, overlap, edge cases
- `PdfTextExtractorTests` — Verify PDF text extraction
- `SmsParserTests` — Verify CSV/JSON SMS parsing
- `HtmlTextExtractorTests` — Verify HTML noise stripping
- `WaybackMachinePluginTests` — Verify API response handling
- `VectorSearchPluginTests` — Verify embedding + search logic

### 9.2 Integration Test Coverage

- Upload endpoint → verify document chunks created in DB
- Agent query endpoint → verify plugin routing with mocked LLM
- Tenant isolation → verify cross-tenant data is inaccessible
- Health check → verify system readiness
