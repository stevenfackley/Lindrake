# Contributing to OmniSift

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker & Docker Compose](https://docs.docker.com/get-docker/)
- An editor with C# support (Visual Studio, Rider, or VS Code with C# Dev Kit)

### Local Setup

```bash
git clone https://github.com/stevenfackley/OmniSift.git
cd OmniSift
```

`.env.dev` is committed with safe defaults. Fill in your personal API keys:

```bash
# Open .env.dev and set:
ANTHROPIC_API_KEY=sk-ant-...
OPENAI_API_KEY=sk-...
TAVILY_API_KEY=tvly-...
```

Start the database and build:

```bash
docker compose up -d db
dotnet restore OmniSift.sln
dotnet build OmniSift.sln
```

### Running Tests

```bash
dotnet test OmniSift.sln
```

Integration tests use EF Core InMemory and a fake chat-completion service — no running PostgreSQL instance or API keys are required.

### Running the Full Stack

```bash
docker compose --env-file .env.dev up --build
```

`--env-file .env.dev` is required: the compose file interpolates the database name, password and JWT secret from the Compose environment, and without it the API cannot start.

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
docker compose build api web    # tags omnisift-api:latest and omnisift-web:latest
IMAGE_API=omnisift-api IMAGE_WEB=omnisift-web \
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
src/OmniSift.Api/               # ASP.NET Core backend
src/OmniSift.Web/               # Blazor WebAssembly frontend
src/OmniSift.Shared/            # Shared DTOs and contracts
tests/OmniSift.UnitTests/       # xUnit unit tests
tests/OmniSift.IntegrationTests/ # xUnit integration tests
infra/db/                       # Superuser-only database setup (extensions, app role, grants)
docs/                           # PDD, SDD, deploy guide, schema provisioning, roadmap
secrets/                        # Docker secret files (*.txt gitignored)
.env.dev                        # Dev environment defaults
.env.test                       # Test/CI environment defaults
.env.example                    # Full variable reference
```

## Reporting Issues

Open an issue describing the problem, steps to reproduce, and expected vs. actual behaviour.
