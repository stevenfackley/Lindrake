# Schema Provisioning Analysis

**Status:** implemented (2026-06-18); updated 2026-10-14 (later migrations, `pg_trgm` moved into `init.sql`, role password, production on qavren-db)

This page is about the **local compose `db`**, where `infra/db/init.sql` plays the provisioning role. Deployments use qavren-db instead: see [Production: qavren-db](#production-qavren-db-2026-10-14).

## The conflict

Two mechanisms both provision the Lindrake schema on a fresh database:

| Mechanism | Runs as | When | Owns |
|-----------|---------|------|------|
| `infra/db/init.sql` | The image's bootstrap superuser (`POSTGRES_USER`, `lindrake` in the compose files) | `docker-entrypoint-initdb.d` at container first-boot (empty data volume only) | Every extension (`uuid-ossp`, `vector`, `pg_trgm`), role creation, GRANTs. |
| EF migrations (`InitialCreate` and later) | `lindrake_app` (non-superuser) | `InitializeLindrake` (`Extensions/WebApplicationExtensions.cs`) → `Database.Migrate()` at API startup | Tables, indexes (HNSW, `pg_trgm` GIN), RLS policies, seed tenant. (`InitialCreate` and `AddPgTrgmExtensionAndIndex` also emit `CREATE EXTENSION IF NOT EXISTS` for the three extensions — those no-op because `init.sql` created them first.) |

Migrations after `InitialCreate`: `AddPgTrgmExtensionAndIndex` (extension + GIN trigram index on `document_chunks.content`), `AddAuditLogAndUsers` (`audit_log`, `users`), `AddDataSourcePiiFlags` (`has_pii`, `pii_flags`), `AddAuditLogRowLevelSecurity` (append-only RLS on `audit_log`), `FailClosedTenantPolicies` (the `InitialCreate` policies read the tenant through `NULLIF(..., '')`, so a pooled connection reset by Npgsql matches no rows instead of failing the `uuid` cast).

On a fresh `docker compose up` both ran in sequence, creating duplicate tables, indexes, and RLS policies. Postgres `CREATE TABLE` is not idempotent — the second run threw errors that were swallowed inside `InitialCreate` only because Migrate() detects the migration is already applied (it checks `__EFMigrationsHistory`). But this was fragile: if the table creation order or the history table raced, errors surfaced.

## Decision: EF is the single source of truth

EF wins because:
- Its migrations are versioned, diffable, and reversible.
- `InitialCreate` already owns the full schema definition in C# types; keeping a parallel SQL copy was duplication waiting to drift.
- EF's snapshot prevents accidental column re-ordering or type mismatches.

## What init.sql keeps (and why it must)

`lindrake_app` is a non-superuser login role. In PostgreSQL 16, `CREATE EXTENSION` for an untrusted extension requires superuser, and a trusted one (such as `pg_trgm`) requires the `CREATE` privilege on the database, which `lindrake_app` does not have: either way `lindrake_app` cannot create the extensions it uses. The superuser is only available during `docker-entrypoint-initdb.d`. So the split is:

1. `init.sql` (superuser, runs first):
   - `CREATE EXTENSION IF NOT EXISTS` for `uuid-ossp`, `vector` and `pg_trgm` — **must be here** because `lindrake_app` can create none of them. A migration that needs a new extension needs a matching line in `init.sql` (and a one-off superuser `CREATE EXTENSION` on databases that already exist).
   - Creates the `lindrake_app` role (idempotent), with the password in the db container's `POSTGRES_APP_PASSWORD` environment variable, read with psql's `\getenv` (the image runs init files through psql). Dev: `lindrake_dev` from `.env.dev`. Unset (production): the role has no password and cannot log in until the superuser sets one. The API must connect as this role: the superuser bypasses RLS.
   - `GRANT USAGE, CREATE ON SCHEMA public TO lindrake_app` — in PG15+, the public schema no longer grants CREATE to PUBLIC by default. `lindrake_app` needs CREATE to run EF's `CREATE TABLE` statements (including `__EFMigrationsHistory`).
   - GRANTs current + future table/sequence privileges via `ALTER DEFAULT PRIVILEGES FOR ROLE lindrake_app`.

2. EF `InitialCreate` and later migrations (lindrake_app, run second):
   - `InitialCreate`'s `AlterDatabase().Annotation(...)` calls and `AddPgTrgmExtensionAndIndex`'s raw SQL emit `CREATE EXTENSION IF NOT EXISTS` — those are safe because Postgres short-circuits the `IF NOT EXISTS` check before the privilege check when the extension already exists, so the non-superuser migration succeeds without error.
   - All tables, indexes (including HNSW), RLS policies.
   - Seed tenant row.

## ALTER DEFAULT PRIVILEGES — what it actually does

The `ALTER DEFAULT PRIVILEGES FOR ROLE lindrake_app` block sets the privilege template that Postgres applies when `lindrake_app` creates future objects. Since `lindrake_app` owns the tables it creates via EF Migrate(), it already has full rights on those tables. These defaults are **future-proofing stubs**: if a second role (e.g. a read-only reporting role) is added later, granting it rights on existing tables and adding a matching default privilege ensures newly migrated tables are covered automatically without another superuser script. The `FOR ROLE lindrake_app` clause requires the caller to be a superuser or a member of `lindrake_app` — init scripts run as the bootstrap superuser, so this runs fine in `docker-entrypoint-initdb.d`.

## What was removed from init.sql (2026-06-18 correction)

The original refactor removed the `CREATE EXTENSION` blocks with the intent that EF would handle them. This was incorrect: in PG16, `CREATE EXTENSION` requires superuser, and EF runs as the non-superuser `lindrake_app`. On a fresh `docker compose up`, the migration would fail with `must be superuser to create this extension`. The extensions are now back in `init.sql` where they belong.

The following were correctly removed and remain owned by EF:
- `CREATE TABLE` blocks for all four tables — EF handles these.
- All `CREATE INDEX` statements — EF handles these (including the HNSW raw-SQL block).
- All `ALTER TABLE … ENABLE ROW LEVEL SECURITY` and `CREATE POLICY` blocks — EF handles these.
- `INSERT INTO tenants` seed row — EF's `InitialCreate.InsertData()` handles this.

## Schema differences between old init.sql and EF

The `tenants` table in the old `init.sql` was missing the `api_key_hash VARCHAR(64)` column that EF added. This means any environment relying on init.sql alone was missing that column — RLS-guarded operations that needed the hash would fail silently. EF is now the authoritative definition.

## pg_trgm moved into init.sql (2026-10-14)

Until 2026-10-14 `init.sql` created `uuid-ossp` and `vector` but not `pg_trgm`, so on a fresh database the `AddPgTrgmExtensionAndIndex` migration (`CREATE EXTENSION IF NOT EXISTS pg_trgm` as `lindrake_app`) failed and the API did not start:

```
ERROR:  permission denied to create extension "pg_trgm"
HINT:  Must have CREATE privilege on current database to create this extension.
```

`init.sql` now creates it next to the other two. A database initialized before that fix already has `pg_trgm` if its API ever started; if it never did, create the extension once as the superuser (`CREATE EXTENSION IF NOT EXISTS pg_trgm;`) or recreate the volume.

## Production: qavren-db (2026-10-14)

Deployments do not run `init.sql`. Their database is qavren-db, the shared Supabase Postgres, where provisioning is split the same way, by other owners:

| Concern | Local compose `db` | qavren-db |
|---|---|---|
| Extensions (`uuid-ossp`, `vector`, `pg_trgm`) | `init.sql`, in `public` | Enabled once project-wide by the qavren-db admin, in the `extensions` schema; declare them in qavren-db's `apps/lindrake.yaml` |
| Login role | `lindrake_app` (`init.sql`) | `lindrake` (`qavren-db/tools/provision-app.ps1`): no superuser, no BYPASSRLS, `search_path = lindrake, extensions` |
| Schema | `public` (`GRANT USAGE, CREATE` to `lindrake_app`) | `lindrake`, owned by the role; no CREATE on `public` |
| Tables, indexes, RLS, seed | EF migrations as `lindrake_app` | EF migrations as `lindrake` |
| `Database:Schema` | `public` (default) | `lindrake` (set by `docker-compose.prod.yml`) |

The migrations name no schema, so their tables land in the first `search_path` schema, and the extension types and operator classes (`vector`, `vector_cosine_ops`, `gin_trgm_ops`, `word_similarity`) resolve through `extensions` in the role's `search_path`. `LindrakeDbContext` deliberately has no `HasDefaultSchema`: it would change the model snapshot (EF reports pending model changes and would move every table). `Database:Schema` instead places `__EFMigrationsHistory` (`MigrationsHistoryTable`) and is compared with `current_schema()` before startup migrations run, so a role whose `search_path` points elsewhere stops the boot instead of scattering tables. The history table's schema exists before the first migration (the role owns it), so the role needs no database-level CREATE.

## Verification path

### Postgres integration suite (both layouts)

`tests/Lindrake.IntegrationTests/Postgres` creates a database per test class from a superuser connection, provisions it like the compose `db` (`PublicSchemaPostgresFixture`, mirroring `init.sql`) or like qavren-db (`QavrenSchemaPostgresFixture`, mirroring `provision.sql.tmpl` with extensions in `extensions`), and runs the migrations through the API's own startup code as a non-superuser role. CI runs it on every PR; locally:

```bash
docker compose up -d db
ConnectionStrings__PostgreSQL="Host=localhost;Port=5432;Database=lindrake;Username=lindrake;Password=lindrake_dev" \
  dotnet test tests/Lindrake.IntegrationTests --filter "FullyQualifiedName~Lindrake.IntegrationTests.Postgres"
```

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

# The lindrake_app role won't exist, so connect as postgres.
# Set connection string to use postgres user for the migrate step:
dotnet ef database update \
  --project src/Lindrake.Api \
  --connection "Host=localhost;Port=5433;Database=lindrake;Username=postgres;Password=test"
```
This proves EF can stand alone without init.sql. The tradeoff: in production you'd still need init.sql (or a one-time superuser script) to create `lindrake_app` before the app starts.

## TODO

- `init.sql` serves the local compose `db` only since production moved to qavren-db (2026-10-14); the role-creation and password steps it once needed for production are gone with the prod `db` container.
- One role both migrates and serves requests (as on qavren-db, where the app role owns its schema). Splitting a DDL owner from a DML-only runtime role would use `ConnectionStrings:Migrations`; qavren-db's provisioning would have to create the second role.
