# Testing

| Layer | Command | What it covers |
|---|---|---|
| Backend unit + HTTP integration | `dotnet test` (repo root) | Domain rules and workflow; migrations (SQLite, snapshot drift for both providers); claim API incl. concurrency, search/sort, uploads; policy ingestion API; the RAG library (extraction, chunking, BM25, embeddings, fusion, reranking, evaluation set, sufficiency, conflicts, citation validation, provider retry/timeout/auth behaviour); the whole summary → review → decision workflow over real HTTP with a scripted language model |
| Frontend | `cd web && npm test` | API client error handling, claims list filters/sort/pagination/debounce, claim form validation, claim detail workflow (named reviewer, notes, version, blocked decisions), AI summary rendering (findings, citations, abstained, failed, conflicts), review controls, policy upload and search |
| Type check / build | `cd web && npm run build` | `tsc --noEmit` (strict) then a production build |
| Browser end-to-end | see `e2e/README.md` | The real UI against a running API in headless Chromium (25 checks) |
| PostgreSQL | manual (below) | Migrations and the workflow on a real server |

Integration tests start the real API in memory (`WebApplicationFactory`) against a throw-away SQLite file and a temp
claim-file directory, so they exercise validation, controllers, services, EF Core and storage together. The AI paths use
the real extractive pipeline, or a scripted `ISummaryGenerator` that stands in for a language model (fabricated
citations, decision language, provider failure).

## PostgreSQL check (manual)

```bash
createdb claimwise_dev   # user/password as you like
Database__Provider=PostgreSQL \
ConnectionStrings__Default="Host=localhost;Database=claimwise_dev;Username=claimwise;Password=…" \
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/ClaimWise.Api
```
EF logs one "Failed executing DbCommand … FROM __EFMigrationsHistory" line on a brand-new database: it is the probe for
the history table before EF creates it, and is expected.

## Adding a migration

```bash
dotnet tool install --global dotnet-ef
cd src/ClaimWise.Api
dotnet ef migrations add <Name> --context SqliteClaimWiseDbContext   --output-dir Data/Migrations/Sqlite
dotnet ef migrations add <Name> --context PostgresClaimWiseDbContext --output-dir Data/Migrations/Postgres
```
A test fails if either provider's model differs from its migration snapshot.
