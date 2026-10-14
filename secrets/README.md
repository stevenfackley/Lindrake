# Secrets

This directory holds Docker file-based secrets used by `docker-compose.prod.yml`.

**`*.txt` files are gitignored — never commit secret values.**

## Required files

Create each file containing only the secret value (no trailing newline needed):

| File | Content |
|---|---|
| `postgres_password.txt` | PostgreSQL password for the `omnisift` superuser |
| `Anthropic__ApiKey.txt` | Anthropic API key (`sk-ant-...`) |
| `OpenAI__ApiKey.txt` | OpenAI API key (`sk-...`); only used with `Embedding__Provider=OpenAi`, may be empty |
| `Tavily__ApiKey.txt` | Tavily search API key (`tvly-...`) |
| `Jwt__Secret.txt` | HS256 signing secret, used only when `Supabase__Url` is empty and `JWT__ALLOWLOCALIDENTITYPROVIDER=true` (min 32 bytes — generate with `openssl rand -base64 48`) |
| `ConnectionStrings__DefaultConnection.txt` | Full Npgsql connection string (see below) |

### Connection string format

```
Host=db;Port=5432;Database=omnisift;Username=omnisift_app;Password=<omnisift_app password>
```

The app connects as `omnisift_app` (not the superuser) so Row-Level Security applies. `infra/db/init.sql` creates that role with the fixed password `omnisift_dev`, independent of `postgres_password.txt`; set a real one with `ALTER ROLE omnisift_app WITH PASSWORD '...'` before the first API start (see `docs/DEPLOY.md`, First-time database setup).

## Quick setup for local prod testing

```bash
mkdir -p secrets
echo "your_postgres_password"              > secrets/postgres_password.txt
echo "sk-ant-..."                          > secrets/Anthropic__ApiKey.txt
echo "sk-..."                              > secrets/OpenAI__ApiKey.txt
echo "tvly-..."                            > secrets/Tavily__ApiKey.txt
openssl rand -base64 48                    > secrets/Jwt__Secret.txt
echo "Host=db;Port=5432;Database=omnisift;Username=omnisift_app;Password=your_app_password" \
                                           > secrets/ConnectionStrings__DefaultConnection.txt
```

Then follow `docs/DEPLOY.md`: write `.env.prod` (at least `POSTGRES_PASSWORD=` empty and `Supabase__Url` or `JWT__ALLOWLOCALIDENTITYPROVIDER=true`), start `db`, set the `omnisift_app` password to `your_app_password` and create `pg_trgm` as the superuser, then:

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
