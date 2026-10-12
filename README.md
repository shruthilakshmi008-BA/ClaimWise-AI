# ClaimWise AI

A claims-review application for healthcare-style claims. A reviewer logs a claim, sees what is missing, uploads supporting
documents, retrieves **cited evidence from policy documents** with hybrid search, reads a grounded summary that **abstains
when the evidence is weak**, reviews it, and records the decision. Every step is audited.

> **All data is synthetic.** Names, IDs, providers, claims and the policy documents in `samples/policies` are fictional.
> Do not enter real patient or insurance data. This is a demonstration project, not medical advice, a coverage-decision
> tool or a compliance-certified system (no HIPAA or other regulatory claim is made).

**Humans decide.** The AI can retrieve and quote policy text. It has no code path that approves, rejects or otherwise
changes a claim, and a claim cannot be approved or rejected while an AI summary with findings is waiting for a person to
accept or reject it.

## What it does

| Area | Implemented |
|---|---|
| Claims | Intake with server-side validation, list with search / filters / sorting / pagination, dashboard, status workflow with allowed transitions, optimistic concurrency, required-document and missing-field checks per claim type, real file upload and download, audit history |
| Policies | Upload `.txt .md .pdf .docx`; text extraction; clause-aware chunking that keeps clause, section, page and offsets; duplicate refusal; activate/deactivate; passage viewer; re-index |
| Retrieval | BM25 keyword + vector search → Reciprocal Rank Fusion → reranker; per-stage scores exposed; document scoping |
| AI summaries | Evidence-sufficiency check and abstention, conflict detection (e.g. base policy vs amendment), citation validation against the real passages (verbatim quotes, grounded figures, no decision language), failure handling |
| Providers | Embeddings: local offline embedder or any OpenAI-compatible endpoint. Summaries: extractive (no model), OpenAI-compatible, or Anthropic (official SDK) |
| Review | Accept/reject each AI summary (named reviewer, note to reject); decisions audited together with the AI summaries a person reviewed |
| Data | EF Core migrations for **SQLite** (default) and **PostgreSQL** |
| UI | React + TypeScript, responsive, accessible, dark-mode aware |

See [docs/implementation-status.md](docs/implementation-status.md) for exactly what was verified and what was not, and
[docs/rag.md](docs/rag.md) for how retrieval and grounding work, with their limitations.

## Quick start (offline, no API keys)

Requirements: **.NET 10 SDK** and **Node 20.19+ or 22+**. (Linux/macOS/Windows.)

```bash
# 1. Backend: restore, build, test
dotnet restore && dotnet build && dotnet test

# 2. Run the API (Development seeds 8 synthetic claims and ingests the 4 sample policies on first start)
cd src/ClaimWise.Api && dotnet run          # http://localhost:5080  (docs: /scalar/v1)

# 3. In a second terminal: the UI
cd web && npm install && npm run dev        # http://localhost:5173  (proxies /api to :5080)
```

Open <http://localhost:5173>. Try: *Claims → New claim*, upload the three required documents, **Move to In review**, then
under *AI policy evidence* ask “annual outpatient limit and co-payment”. You will see cited passages, a conflict warning
(the 2025 amendment changes the 2024 limit), and Accept/Reject controls; *Approve* stays disabled until the summary is
reviewed. Ask something unrelated (“quantum computing satellite launch insurance”) to see an abstention.

To start from scratch delete the database file (`claimwise.db` in `src/ClaimWise.Api`) and `claim-files/`.
A database created by the very first version (without migrations) is detected at startup with instructions to delete it.

### Frontend commands

```bash
npm run dev        # dev server       npm test          # vitest
npm run build      # tsc --noEmit + production build      npm run typecheck
```

### PostgreSQL

```bash
export Database__Provider=PostgreSQL
export ConnectionStrings__Default="Host=localhost;Database=claimwise;Username=claimwise;Password=…"
cd src/ClaimWise.Api && dotnet run          # migrations are applied at startup
```

### Real language / embedding models

Defaults are fully offline. To use hosted models set the variables in [`.env.example`](.env.example), for example:

```bash
export Ai__Llm__Provider=Anthropic          # key from ANTHROPIC_API_KEY or Ai__Llm__Anthropic__ApiKey (use user-secrets)
export Ai__Embeddings__Provider=OpenAiCompatible
export Ai__Embeddings__OpenAiCompatible__BaseUrl=https://api.openai.com/v1
export Ai__Embeddings__OpenAiCompatible__Model=text-embedding-3-small
export Ai__Embeddings__OpenAiCompatible__Dimensions=1536
export Ai__Embeddings__OpenAiCompatible__ApiKey=…
```
After changing the embedding model, use **Policies → Re-index all**. **These live providers have not been run in this
repository's verification (no credentials); only stubbed/scripted tests cover them.**

## Repository layout

```
ClaimWise.slnx
src/ClaimWise.Rag/     framework-free library: extraction, chunking, BM25, embeddings, fusion, reranker,
                       evidence assessment, conflicts, citation validation, provider abstractions
src/ClaimWise.Api/     ASP.NET Core Web API: domain, EF Core (+ migrations), services, controllers, AI wiring
tests/ClaimWise.Tests/ xUnit unit + HTTP integration + RAG tests
web/                   React + TypeScript UI (Vite, Vitest)
e2e/                   browser end-to-end check (Playwright core)
samples/policies/      fictional policy documents (+ manifest.json used for the Development seed)
docs/                  api, rag, testing, architecture, decisions, requirements, implementation-status
```

## Documentation

[API reference](docs/api.md) · [Retrieval & grounding](docs/rag.md) · [Testing](docs/testing.md) ·
[Architecture](docs/architecture.md) · [Decisions](docs/decisions.md) · [Requirements](docs/requirements.md) ·
[Implementation status](docs/implementation-status.md) · [Config template](.env.example)

## Security and privacy notes

* Server-side validation everywhere; EF Core parameterized queries; whitelisted sort fields; response security headers;
  rate limiting on upload and AI endpoints.
* Uploads: extension allow-list **and** content check, size capped while reading, server-generated storage names
  (the uploaded name is display-only), downloads served as attachments with `nosniff`; DOCX/XML parsing with DTDs
  disabled.
* Secrets belong in environment variables or `dotnet user-secrets`; `.env` files, databases and uploaded files are
  git-ignored. API keys are never logged and provider error messages are sanitised.
* AI safety: citations are validated against retrieved text, abstention before generation when evidence is weak, system
  prompts treat claim/policy text as data, and a human must review any summary with findings.
* **There is no authentication or authorisation yet.** The reviewer name is self-declared, so the audit trail records who
  *said* they acted, not a verified identity. The API must not be exposed beyond a trusted network until login and roles
  exist. Real patient data must never be loaded.

## Known limitations

See [docs/rag.md §8](docs/rag.md) and [docs/implementation-status.md §5](docs/implementation-status.md). Highlights: the local
embedder is not a neural model; the reranker is heuristic; live language-model providers are untested here; no OCR; no
authentication; the in-memory index targets small corpora; SQLite stores money as a double (PostgreSQL uses `numeric`).
