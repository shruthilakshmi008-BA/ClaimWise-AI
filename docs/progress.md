# Project Progress

Legend: ✅ done and verified as described · 🟡 written, not yet verified on a full build · ⬜ not started

## Stage 1: basic claims workflow (backend)

| Item | Status | Notes |
|---|---|---|
| Reuse assessment | ✅ | Based on README + portfolio only; module code not accessible |
| MVP requirements | ✅ | `docs/requirements.md` |
| Architecture + diagrams | ✅ | `docs/architecture.md` |
| Technology decisions | ✅ | `docs/decisions.md` |
| Domain rules and workflow | ✅ | Compiled and exercised with a console check |
| DTOs + input validation | ✅ | Compiled and exercised with a console check |
| Controller + error handler | ✅ | Compiled (not run over HTTP) |
| EF Core context, seed data, ClaimService | 🟡 | Needs NuGet packages; not compiled by the author |
| Unit tests (rules, workflow) | 🟡 | Logic run with a stand-in runner, not real xUnit |
| Integration tests (API over HTTP) | 🟡 | Not run |
| **First `dotnet build` + `dotnet test` on your machine** | ⬜ | **Your next step; send me the full output** |

## Remaining work

| Stage | Item | Status |
|---|---|---|
| 1b | React + TypeScript frontend (dashboard, list, detail, review actions) | ⬜ |
| 1b | Docs: `api.md`, `testing.md`, `user-guide.md` | ⬜ |
| 1c | GitHub setup walkthrough (repo, first commit, push) | ⬜ |
| 2 | Policy ingestion, chunking, hybrid retrieval, reranking | ⬜ |
| 2 | Grounded summaries, citations, abstention, evidence indicator | ⬜ |
| 2 | Retrieval evaluation set and metrics | ⬜ |
| 3 | Authentication/roles, migrations, PostgreSQL, CI, deployment, `deployment.md` | ⬜ |
