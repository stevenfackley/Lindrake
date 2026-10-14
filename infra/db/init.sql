-- ============================================================
-- OmniSift Database Initialization Script
-- PostgreSQL 16 + pgvector
-- ============================================================
--
-- SCOPE — what this file owns:
--   Only what EF Core's migrations cannot do as a non-superuser:
--     1. Every extension the migrations use (uuid-ossp, vector, pg_trgm).
--        omnisift_app can create none of them: untrusted extensions need
--        superuser, trusted ones (pg_trgm) need CREATE on the database.
--     2. The `omnisift_app` application role (password from the
--        POSTGRES_APP_PASSWORD environment variable) + its GRANTs.
--     3. Default privilege rules so future tables auto-grant.
--
-- WHAT RUNS FIRST (on a fresh container):
--   docker-entrypoint-initdb.d runs this file as the postgres
--   superuser before the .NET app starts. That is the only
--   moment a superuser is available to CREATE EXTENSION / CREATE ROLE.
--
-- WHAT RUNS SECOND:
--   Program.cs calls Database.Migrate() at startup, which runs
--   EF's InitialCreate migration as the omnisift_app user.
--   InitialCreate (uuid-ossp, vector) and AddPgTrgmExtensionAndIndex
--   (pg_trgm) also emit `CREATE EXTENSION IF NOT EXISTS` — those are safe
--   only because this file created the extensions first: Postgres
--   short-circuits the IF NOT EXISTS check BEFORE the privilege check.
--   InitialCreate owns: all tables, all indexes (including the HNSW
--   index), RLS policies, and the seed tenant.
--
-- See docs/schema-provisioning.md for the full analysis of why
-- the schema is split this way.
-- ============================================================

-- ============================================================
-- Extensions
-- Must run as superuser. The migrations emit CREATE EXTENSION IF NOT
-- EXISTS for each of these (OmniSiftDbContext.HasPostgresExtension), but
-- as omnisift_app that only succeeds when the extension already exists:
-- Postgres short-circuits before the privilege check. Add any extension a
-- new migration needs here as well.
-- ============================================================
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;   -- keyword arm of hybrid search (GIN trigram index)

-- ============================================================
-- Application Role
-- Create a non-superuser role for the application connection.
-- The superuser bypasses RLS, so the app MUST connect as this
-- role for RLS to be enforced.
--
-- Password: the db container's POSTGRES_APP_PASSWORD (dev value
-- omnisift_dev, set by docker-compose.yml / .env.dev). The image's
-- init runner executes this file with psql, which reads it with
-- \getenv. Unset or empty, as in production (docker-compose.prod.yml):
-- the role is created WITHOUT a password and cannot log in until the
-- superuser runs ALTER ROLE omnisift_app WITH PASSWORD '...'
-- (docs/DEPLOY.md, First-time database setup).
-- Idempotent: nothing happens when the role already exists.
-- ============================================================
\set app_password ''
\getenv app_password POSTGRES_APP_PASSWORD

-- format(%L) quotes the value; NULLIF turns '' into PASSWORD NULL.
SELECT format('CREATE ROLE omnisift_app WITH LOGIN PASSWORD %L', NULLIF(:'app_password', ''))
WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'omnisift_app')
\gexec

SELECT :'app_password' = '' AS app_password_unset \gset
\if :app_password_unset
\warn 'init.sql: POSTGRES_APP_PASSWORD is not set. omnisift_app has no password and cannot log in until the superuser sets one (docs/DEPLOY.md).'
\endif
\unset app_password
\unset app_password_unset

-- USAGE   : required to reference schema-qualified names.
-- CREATE  : required for EF Migrate() to CREATE TABLE (including __EFMigrationsHistory).
-- In PG15+ the public schema no longer grants CREATE to PUBLIC by default;
-- omnisift_app needs it explicitly since it runs all EF migrations.
GRANT USAGE, CREATE ON SCHEMA public TO omnisift_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO omnisift_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO omnisift_app;

-- ALTER DEFAULT PRIVILEGES FOR ROLE omnisift_app seeds the privilege
-- template that Postgres applies whenever omnisift_app creates a new
-- object. Since omnisift_app runs EF's Migrate() and therefore owns the
-- tables it creates, it already has full rights on those tables by
-- virtue of ownership. These defaults are future-proofing stubs: if a
-- second role (e.g. a read-only reporting role) is added later, granting
-- it rights on existing tables + setting a matching default privilege here
-- ensures newly migrated tables are covered automatically — without
-- requiring another superuser script at that point.
--
-- NOTE: `FOR ROLE omnisift_app` requires the caller (postgres) to be
-- superuser OR a member of omnisift_app — postgres is superuser, so
-- this runs fine in docker-entrypoint-initdb.d.
ALTER DEFAULT PRIVILEGES FOR ROLE omnisift_app IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO omnisift_app;
ALTER DEFAULT PRIVILEGES FOR ROLE omnisift_app IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO omnisift_app;
