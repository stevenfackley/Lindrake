# Secrets

This directory holds Docker file-based secrets used by `docker-compose.prod.yml`.

**`*.txt` files are gitignored — never commit secret values.**

## Required files

Create each file containing only the secret value (no trailing newline needed):

| File | Content |
|---|---|
| `Anthropic__ApiKey.txt` | Anthropic API key (`sk-ant-...`) |
| `OpenAI__ApiKey.txt` | OpenAI API key (`sk-...`); only used with `Embedding__Provider=OpenAi`, may be empty |
| `Tavily__ApiKey.txt` | Tavily search API key (`tvly-...`) |
| `Jwt__Secret.txt` | HS256 signing secret, used only when `Supabase__Url` is empty and `JWT__ALLOWLOCALIDENTITYPROVIDER=true` (min 32 bytes — generate with `openssl rand -base64 48`) |
| `ConnectionStrings__DefaultConnection.txt` | Full Npgsql connection string to qavren-db (see below). `deploy.yml` writes it from the `LINDRAKE_DB_URL_PROD` GitHub secret on every deploy |

### Connection string format

Production data lives in qavren-db, the shared Supabase Postgres: schema `lindrake`, login role `lindrake` (provisioned by `qavren-db/tools/provision-app.ps1`, which prints the password once). Connect through the Supavisor **session** pooler, port 5432, with TLS:

```
Host=<pooler host>;Port=5432;Database=postgres;Username=lindrake.<project ref>;Password=<lindrake password>;SSL Mode=Require
```

Use the user name the pooler expects (Supavisor's is `<role>.<project ref>`). Session mode, not the transaction pooler on 6543: the API sets the RLS tenant with a session-level `set_config` when a connection opens (DECISIONS.md, 2026-10-14). The role is not a superuser and has no BYPASSRLS, so Row-Level Security applies.

## Quick setup for local prod testing

```bash
mkdir -p secrets
echo "sk-ant-..."                          > secrets/Anthropic__ApiKey.txt
echo "sk-..."                              > secrets/OpenAI__ApiKey.txt
echo "tvly-..."                            > secrets/Tavily__ApiKey.txt
openssl rand -base64 48                    > secrets/Jwt__Secret.txt
printf '%s' "Host=<pooler host>;Port=5432;Database=postgres;Username=lindrake.<project ref>;Password=<password>;SSL Mode=Require" \
                                           > secrets/ConnectionStrings__DefaultConnection.txt
```

Then follow `docs/DEPLOY.md`: write `.env.prod` (at least `Supabase__Url` or `JWT__ALLOWLOCALIDENTITYPROVIDER=true`), then:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

## CI/CD

In your deployment pipeline, write these files from your secrets manager
(HashiCorp Vault, AWS Secrets Manager, Azure Key Vault, GCP Secret Manager, etc.)
before invoking `docker compose up`.

## How it works

The API reads secret files from `/run/secrets/` at startup via `AddKeyPerFile`.
Filenames use `__` as the section separator, which maps to .NET config keys:
- `Anthropic__ApiKey` → `Anthropic:ApiKey`
- `ConnectionStrings__DefaultConnection` → `ConnectionStrings:DefaultConnection`
