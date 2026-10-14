# Schema Provisioning Analysis

**Status:** implemented (2026-06-18); updated 2026-10-14 (later migrations, `pg_trgm` moved into `init.sql`, role password)

## The conflict

Two mechanisms both provision the OmniSift schema on a fresh database:

| Mechanism | Runs as | When | Owns |
|-----------|---------|------|------|
| `infra/db/init.sql` | The image's bootstrap superuser (`POSTGRES_USER`, `omnisift` in the compose files) | `docker-entrypoint-initdb.d` at container first-boot (empty data volume only) | Every extension (`uuid-ossp`, `vector`, `pg_trgm`), role creation, GRANTs. |
| EF migrations (`InitialCreate` and later) | `omnisift_app` (non-superuser) | `InitializeOmniSift` (`Extensions/WebApplicationExtensions.cs`) → `Database.Migrate()` at API startup | Tables, indexes (HNSW, `pg_trgm` GIN), RLS policies, seed tenant. (`InitialCreate` and `AddPgTrgmExtensionAndIndex` also emit `CREATE EXTENSION IF NOT EXISTS` for the three extensions — those no-op because `init.sql` created them first.) |

Migrations after `InitialCreate`: `AddPgTrgmExtensionAndIndex` (extension + GIN trigram index on `document_chunks.content`), `AddAuditLogAndUsers` (`audit_log`, `users`), `AddDataSourcePiiFlags` (`has_pii`, `pii_flags`), `AddAuditLogRowLevelSecurity` (append-only RLS on `audit_log`).

On a fresh `docker compose up` both ran in sequence, creating duplicate tables, indexes, and RLS policies. Postgres `CREATE TABLE` is not idempotent — the second run threw errors that were swallowed inside `InitialCreate` only because Migrate() detects the migration is already applied (it checks `__EFMigrationsHistory`). But this was fragile: if the table creation order or the history table raced, errors surfaced.

## Decision: EF is the single source of truth

EF wins because:
- Its migrations are versioned, diffable, and reversible.
- `InitialCreate` already owns the full schema definition in C# types; keeping a parallel SQL copy was duplication waiting to drift.
- EF's snapshot prevents accidental column re-ordering or type mismatches.

## What init.sql keeps (and why it must)

`omnisift_app` is a non-superuser login role. In PostgreSQL 16, `CREATE EXTENSION` for an untrusted extension requires superuser, and a trusted one (such as `pg_trgm`) requires the `CREATE` privilege on the database, which `omnisift_app` does not have: either way `omnisift_app` cannot create the extensions it uses. The superuser is only available during `docker-entrypoint-initdb.d`. So the split is:

1. `init.sql` (superuser, runs first):
   - `CREATE EXTENSION IF NOT EXISTS` for `uuid-ossp`, `vector` and `pg_trgm` — **must be here** because `omnisift_app` can create none of them. A migration that needs a new extension needs a matching line in `init.sql` (and a one-off superuser `CREATE EXTENSION` on databases that already exist).
   - Creates `omnisift_app` role (idempotent `DO $$` block).
   - `GRANT USAGE, CREATE ON SCHEMA public TO omnisift_app` — in PG15+, the public schema no longer grants CREATE to PUBLIC by default. `omnisift_app` needs CREATE to run EF's `CREATE TABLE` statements (including `__EFMigrationsHistory`).
   - GRANTs current + future table/sequence privileges via `ALTER DEFAULT PRIVILEGES FOR ROLE omnisift_app`.

2. EF `InitialCreate` and later migrations (omnisift_app, run second):
   - `InitialCreate`'s `AlterDatabase().Annotation(...)` calls and `AddPgTrgmExtensionAndIndex`'s raw SQL emit `CREATE EXTENSION IF NOT EXISTS` — those are safe because Postgres short-circuits the `IF NOT EXISTS` check before the privilege check when the extension already exists, so the non-superuser migration succeeds without error.
   - All tables, indexes (including HNSW), RLS policies.
   - Seed tenant row.

## ALTER DEFAULT PRIVILEGES — what it actually does

The `ALTER DEFAULT PRIVILEGES FOR ROLE omnisift_app` block sets the privilege template that Postgres applies when `omnisift_app` creates future objects. Since `omnisift_app` owns the tables it creates via EF Migrate(), it already has full rights on those tables. These defaults are **future-proofing stubs**: if a second role (e.g. a read-only reporting role) is added later, granting it rights on existing tables and adding a matching default privilege ensures newly migrated tables are covered automatically without another superuser script. The `FOR ROLE omnisift_app` clause requires the caller to be a superuser or a member of `omnisift_app` — init scripts run as the bootstrap superuser, so this runs fine in `docker-entrypoint-initdb.d`.

## What was removed from init.sql (2026-06-18 correction)

The original refactor removed the `CREATE EXTENSION` blocks with the intent that EF would handle them. This was incorrect: in PG16, `CREATE EXTENSION` requires superuser, and EF runs as the non-superuser `omnisift_app`. On a fresh `docker compose up`, the migration would fail with `must be superuser to create this extension`. The extensions are now back in `init.sql` where they belong.

The following were correctly removed and remain owned by EF:
- `CREATE TABLE` blocks for all four tables — EF handles these.
- All `CREATE INDEX` statements — EF handles these (including the HNSW raw-SQL block).
- All `ALTER TABLE … ENABLE ROW LEVEL SECURITY` and `CREATE POLICY` blocks — EF handles these.
- `INSERT INTO tenants` seed row — EF's `InitialCreate.InsertData()` handles this.

## Schema differences between old init.sql and EF

The `tenants` table in the old `init.sql` was missing the `api_key_hash VARCHAR(64)` column that EF added. This means any environment relying on init.sql alone was missing that column — RLS-guarded operations that needed the hash would fail silently. EF is now the authoritative definition.

## pg_trgm moved into init.sql (2026-10-14)

Until 2026-10-14 `init.sql` created `uuid-ossp` and `vector` but not `pg_trgm`, so on a fresh database the `AddPgTrgmExtensionAndIndex` migration (`CREATE EXTENSION IF NOT EXISTS pg_trgm` as `omnisift_app`) failed and the API did not start:

```
ERROR:  permission denied to create extension "pg_trgm"
HINT:  Must have CREATE privilege on current database to create this extension.
```

`init.sql` now creates it next to the other two. A database initialized before that fix already has `pg_trgm` if its API ever started; if it never did, create the extension once as the superuser (`CREATE EXTENSION IF NOT EXISTS pg_trgm;`) or recreate the volume.

## Verification path

### Docker path (init.sql + Migrate())
```bash
docker compose --env-file .env.dev down -v      # wipe volume
docker compose --env-file .env.dev up -d db     # init.sql runs
docker compose --env-file .env.dev up -d api    # Migrate() runs every migration
docker compose logs api | grep "Applied migration"
```
Expected: each migration applied once; no duplicate-object errors.

### EF-only path (no init.sql, e.g. CI against a bare pgvector container)
```bash
# Start a bare pgvector container with a superuser-only connection string
docker run -d --name pg-test \
  -e POSTGRES_PASSWORD=test \
  -p 5433:5432 \
  pgvector/pgvector:pg16

# The omnisift_app role won't exist, so connect as postgres.
# Set connection string to use postgres user for the migrate step:
dotnet ef database update \
  --project src/OmniSift.Api \
  --connection "Host=localhost;Port=5433;Database=omnisift;Username=postgres;Password=test"
```
This proves EF can stand alone without init.sql. The tradeoff: in production you'd still need init.sql (or a one-time superuser script) to create `omnisift_app` before the app starts.

## TODO: production hardening

- `omnisift_dev` is hard-coded as the `omnisift_app` password in init.sql, independent of `POSTGRES_PASSWORD` (which only sets the bootstrap superuser's password). Production deployments must run `ALTER ROLE omnisift_app WITH PASSWORD '...'` as the superuser and put that password in the `ConnectionStrings__DefaultConnection` secret (see `docs/DEPLOY.md`, First-time database setup).
- Consider extracting the role-creation block into a separate `infra/db/create-role.sql` script that can be run by a DBA without re-running the full init sequence.
