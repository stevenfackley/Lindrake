# Contributing to Lindrake

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker & Docker Compose](https://docs.docker.com/get-docker/)
- An editor with C# support (Visual Studio, Rider, or VS Code with C# Dev Kit)

### Local Setup

```bash
git clone https://github.com/stevenfackley/Lindrake.git
cd Lindrake
```

`.env.dev` is committed with safe defaults. Optionally fill in your personal API keys:

```bash
# Open .env.dev and set:
ANTHROPIC_API_KEY=sk-ant-...
OPENAI_API_KEY=sk-...
TAVILY_API_KEY=tvly-...
```

Start the database and build:

```bash
docker compose up -d db
dotnet restore Lindrake.sln
dotnet build Lindrake.sln
```

### Running Tests

```bash
dotnet test Lindrake.sln
```

Integration tests use a fake chat-completion service and need no API keys. Most run on EF Core InMemory. The Postgres suite (`tests/Lindrake.IntegrationTests/Postgres`) needs a superuser connection to a pgvector server in `ConnectionStrings__PostgreSQL` and is skipped without it; CI always runs it. Against the compose `db` (Git Bash; in PowerShell set `$env:ConnectionStrings__PostgreSQL` first):

```bash
docker compose up -d db
ConnectionStrings__PostgreSQL="Host=localhost;Port=5432;Database=lindrake;Username=lindrake;Password=lindrake_dev" \
  dotnet test tests/Lindrake.IntegrationTests --filter "FullyQualifiedName~Lindrake.IntegrationTests.Postgres"
```

Each test class creates and drops its own database (`lindrake_it_<id>`); the suite also creates the roles `lindrake_test_app` and `lindrake_qdb`, which stay until `docker compose down -v`.

### Running the Full Stack

```bash
docker compose up --build
```

The compose file defaults every value it interpolates to its `.env.dev` value, so this needs no `--env-file`. Compose does not read your edits to `.env.dev` (API keys, passwords) unless you pass `--env-file .env.dev` to the compose command.

| Service | URL |
|---|---|
| Landing page | http://localhost:5080 |
| App (Blazor) | http://localhost:5080/app/ |
| Backend API | http://localhost:5000 |
| Swagger (Development only) | http://localhost:5000/swagger |
| PostgreSQL | localhost:5432 |

### Running with Production Config (local)

See `secrets/README.md` for how to populate the required secret files and write a `.env.prod` (see `docs/DEPLOY.md`). The overlay removes `build` and runs the GHCR images; to run locally built ones instead, build with the base file and point the overlay at them:

```bash
docker compose build api web    # tags lindrake-api:latest and lindrake-web:latest
IMAGE_API=lindrake-api IMAGE_WEB=lindrake-web \
  docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

## Branch & PR Workflow

- Branch from `main` using the pattern `feat/`, `fix/`, or `chore/` followed by a short description
- Keep PRs focused on a single concern
- All CI checks must pass before merging

## Code Style

- Follow standard C# conventions (PascalCase types/methods, camelCase locals)
- Async methods must be suffixed with `Async`
- Do not commit secrets, build artifacts, or `.env.prod` — the `.gitignore` covers these
- `.env.dev` and `.env.test` are safe to commit (no real secrets)

## Project Structure

```
src/Lindrake.Api/               # ASP.NET Core backend
src/Lindrake.Web/               # Blazor WebAssembly frontend
src/Lindrake.Shared/            # Shared DTOs and contracts
tests/Lindrake.UnitTests/       # xUnit unit tests
tests/Lindrake.IntegrationTests/ # xUnit integration tests
infra/db/                       # Superuser-only database setup (extensions, app role, grants)
docs/                           # PDD, SDD, deploy guide, schema provisioning, roadmap
secrets/                        # Docker secret files (*.txt gitignored)
.env.dev                        # Dev environment defaults
.env.test                       # Test/CI environment defaults
.env.example                    # Full variable reference
```

## Reporting Issues

Open an issue describing the problem, steps to reproduce, and expected vs. actual behaviour.
