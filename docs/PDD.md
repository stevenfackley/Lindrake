# Lindrake — Product Design Document (PDD)

**Version:** 1.1
**Date:** 2026-10-14 (first issued 2026-02-25)
**Status:** Active Development

---

## Revision 2026-10-14

Brought in line with what has shipped. What changed:

- **Ingestion:** besides PDF, SMS exports and web pages, uploads now accept Word (`.docx`), email (`.eml`, `.mbox`) and plain text (`.txt`, `.md`). Uploads are processed in the background and report `pending` → `processing` → `completed`/`failed`. Chunks are 480 tokens (48 overlap) measured by the embedding model, not 500.
- **Search:** document search is hybrid (meaning + exact words), not vector similarity alone.
- **Agent:** answers stream token by token; conversations export as cited Markdown or PDF reports; the research persona (prompt, tools, report title) is configurable per deployment.
- **Shipped since 1.0:** entity relationship graph and timeline, PII flagging, per-tenant audit log, account data export and erase, role-based onboarding with a sample corpus.
- **Not built yet:** chunk explorer and an in-app search-history view (history is stored and included in the account export); team invites (each self-service sign-up creates its own workspace).
- **Security:** encryption at rest for document content is deferred (DECISIONS.md, 2026-06-30); data is encrypted in transit and isolated per tenant in the application and the database.
- **Roadmap:** v1.1/v1.2 items updated with their status.

---

## 1. Product Vision

Lindrake is an AI-powered research platform that empowers individuals, genealogists, journalists, and investigators to aggregate, search, and synthesize personal and family history data from multiple sources — both private documents and public web records — into a unified, intelligent knowledge base.

The platform acts as a tireless research assistant that can independently search across uploaded documents, live web sources, and archived internet snapshots to answer complex, multi-step research questions about people and their histories.

## 2. Problem Statement

Researching personal and family histories is a fragmented, labor-intensive process:

- **Data is scattered** across physical documents, digital files, text messages, web pages, and archived sources
- **Manual searching** through hundreds of documents for relevant details is time-consuming and error-prone
- **Web sources disappear** — critical obituaries, news articles, and public records go offline with no way to recover them
- **Connecting the dots** between disparate pieces of information requires significant cognitive effort
- **No unified tool** exists that combines document ingestion, web search, archive retrieval, and AI-powered synthesis

## 3. Target Audience

### Primary Users
| Persona | Description | Key Need |
|---|---|---|
| **Genealogists** | Amateur and professional family history researchers | Aggregate family records and public data into timelines |
| **Journalists** | Investigative reporters researching individuals | Cross-reference public records with private source documents |
| **Legal Professionals** | Paralegals and attorneys building case profiles | Search and synthesize large document collections efficiently |
| **Private Investigators** | Licensed PIs conducting background research | Combine web search with document analysis |

### Secondary Users
| Persona | Description | Key Need |
|---|---|---|
| **Individuals** | People researching their own family history | Upload and search personal documents with AI assistance |
| **Archivists** | Digital preservation professionals | Bulk ingest and semantically index document collections |

## 4. Core Features

### 4.1 Document Ingestion & Processing
- **File Upload** — PDF, Word (`.docx`), email (`.eml`, `.mbox`), plain text (`.txt`, `.md`), HTML and text-message exports (CSV/JSON), up to 50 MB each. Processing runs in the background; the data source list shows its status and any failure reason.
- **Text Message Import** — CSV/JSON exports normalized to one `sender (timestamp) message` line per message
- **Web Page Capture** — Provide a URL; article text is extracted, cleaned, and indexed. Only public web addresses can be fetched, pages are capped at 10 MB.
- **Intelligent Chunking** — Content is split into overlapping chunks of up to 480 tokens, sized for the embedding model so nothing is cut off before indexing
- **PII Flagging** — Documents containing personal data are flagged with the detected types; nothing is rejected

### 4.2 AI-Powered Research Agent
- **Natural Language Queries** — Ask complex research questions in plain English
- **Autonomous Tool Selection** — The agent independently decides which tools to use based on the query:
  - Search uploaded documents (hybrid: semantic similarity + exact keyword matching)
  - Execute live web searches
  - Check the Wayback Machine for archived pages
- **Streaming Answers** — Answers appear as they are generated, rendered as Markdown; a running answer can be stopped
- **Source Attribution** — Every answer includes citations to the source documents, web results or archive snapshots
- **Conversational Context** — Multi-turn conversations keep context for follow-up questions (the most recent 20 messages are sent to the model)
- **Cited Reports** — Export a conversation with its citations as Markdown or PDF
- **Configurable Persona** — Each deployment sets the agent's system prompt, available tools and report title through configuration

### 4.3 Multi-Tenant Workspace
- **Isolated Workspaces** — Each tenant's data is isolated in the application and by database row-level security
- **Team Collaboration** — The data model allows several users per tenant (with Supabase, every user whose token carries the tenant's id). Self-service sign-up creates a new workspace per account; an invite flow is not built yet.
- **Data Sovereignty** — Tenant data never leaks across organizational boundaries

### 4.4 Knowledge Management
- **Data Source Registry** — Track all ingested sources with type, upload date, status, chunk count and PII flags
- **Entity Graph & Timeline** — Build a relationship graph and timeline of the people, places and events in a tenant's documents on demand
- **Audit Log** — Uploads, deletions, queries and account actions are recorded per tenant
- **Data Export & Erase** — Export a tenant's data sources, query history and audit log, or erase all of it
- **Chunk Explorer** — *Not built yet.* Browse and inspect individual document chunks
- **Search History** — *Stored, not yet browsable in the app.* Past queries and answers are saved and included in the data export

## 5. User Journeys

### Journey 1: Document Upload & Research
```
1. User logs in → lands on Dashboard
2. User clicks "Upload" → selects PDF files
3. System accepts each file and ingests it in the background (status shown and refreshed)
4. User navigates to "Research" chat interface
5. User asks: "What addresses did John Smith live at between 1990 and 2005?"
6. Agent searches the user's documents (semantic + keyword) → synthesizes an answer
7. Answer streams in with source citations; the conversation can be exported as a report
```

### Journey 2: Web-Augmented Research
```
1. User asks: "Find recent obituaries for Mary Johnson in Ohio"
2. Agent determines web search is needed → calls the web search tool
3. Agent receives titles, URLs and snippets for matching pages
4. For pages that have gone offline → Agent checks the Wayback Machine for archived versions
5. Agent synthesizes findings with any matching uploaded documents
6. Comprehensive answer displayed with both web and document sources
```

## 6. Non-Functional Requirements

| Requirement | Target |
|---|---|
| **Response Time** | Agent responses within 15 seconds for typical queries; first tokens stream sooner |
| **Upload Processing** | PDFs processed at ~10 pages/second |
| **Concurrent Users** | Support 100+ concurrent users per deployment |
| **Data Retention** | All uploaded data retained until explicitly deleted by tenant |
| **Availability** | 99.5% uptime target for cloud deployments |
| **Security** | Encrypted in transit; strict tenant isolation (application filters + database row-level security); per-tenant rate limits. Encryption at rest of document content is deferred (DECISIONS.md, 2026-06-30) |

## 7. Success Metrics

| Metric | Definition | Target |
|---|---|---|
| **Query Relevance** | % of agent responses rated "helpful" by users | > 80% |
| **Ingestion Success** | % of uploaded documents successfully processed | > 95% |
| **Source Recovery** | % of dead links successfully retrieved via Wayback Machine | > 60% |
| **User Retention** | Monthly active users returning after first month | > 70% |

## 8. Future Roadmap

| Phase | Features | Status (2026-10-14) |
|---|---|---|
| **v1.1** | Image/OCR ingestion (scanned documents), email import (EML/MBOX) | Email import shipped; OCR deferred (native dependencies) |
| **v1.2** | Timeline visualization, relationship graph builder | Shipped (entity graph + timeline) |
| **v1.3** | Collaborative annotations, shared research workspaces | Not started |
| **v2.0** | Multi-language support, real-time web monitoring alerts | Not started |

See [marketability-roadmap.md](./marketability-roadmap.md) for the go-to-market items.
