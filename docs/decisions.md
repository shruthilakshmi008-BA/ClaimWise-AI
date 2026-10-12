# Design Decisions and Trade-offs

Each entry says what was chosen, why, what the alternatives were, and the downside.

## D1. .NET 10 (LTS) instead of .NET 8

.NET 8 and .NET 9 both reach end of support on 10 November 2026; .NET 10 is an LTS release supported until November 2028. Starting a new portfolio project on a runtime that is about to lose support would look dated.
*Downside:* some tutorials still show .NET 8, so small differences may appear.

## D2. Controllers instead of minimal APIs

Controllers group related endpoints in a class and are what most .NET job descriptions mention. Minimal APIs are shorter but scatter endpoints. *Downside:* a little more ceremony.

## D3. SQLite now, PostgreSQL at deployment

SQLite needs no server, which suits a beginner on Windows. EF Core lets us switch providers later. *Downside:* SQLite lacks some types and features (see D5), and a SQLite file does not survive on ephemeral cloud hosts.

## D4. `EnsureCreated` now, migrations later

`EnsureCreated` builds the database from the model in one line, so Stage 1 needs no extra tools. It cannot evolve an existing database, so before Stage 2 adds tables we switch to EF Core migrations (which also need the `dotnet-ef` tool). *Downside:* if the model changes during Stage 1, delete `claimwise.db` and let it recreate.

## D5. Money stored as `double` in SQLite

SQLite cannot sort or sum EF Core's `decimal`. The model keeps `decimal` in C# and converts to a double for storage. That is acceptable for 2-decimal demo amounts below 10 million, but it is **not** how real financial systems should store money. PostgreSQL's `numeric` removes the need for the conversion.
*Alternative considered:* storing integer cents. Cleaner for money, but harder for a beginner to read.

## D6. Enums stored as text

`Status = "InReview"` is readable in the database and in JSON. *Downside:* sorting by status would be alphabetical, so status sorting is not offered.

## D7. Business rules in pure static classes

`ClaimRules` and `ClaimWorkflow` have no database or web dependencies, so they are tested in milliseconds. *Downside:* rules are code, not configuration; changing a rule means a code change.

## D8. No repository layer

`DbContext` already implements the unit-of-work and repository ideas, and an extra layer would add files without adding value at this size. This is revisited if the data access becomes complex.

## D9. Exceptions for business-rule failures

The service throws `NotFoundException` / `BusinessRuleException`, and one handler turns them into 404 / 409. This keeps controllers one line long. *Alternative:* a `Result<T>` return type, which is more explicit but more verbose.

## D10. Human decisions only; no authentication yet

Approve/Reject needs a named reviewer and a note. Because there is no login, the name is self-declared, so the audit trail is **not** tamper-proof or identity-verified. Authentication and roles are planned before deployment, and this limitation is stated in the README rather than hidden.

## D11. Documents are metadata only in Stage 1

Uploading and parsing files belongs with ingestion in Stage 2, where text extraction and chunking are needed. Stage 1 still proves the completeness workflow.

## D12. API documentation: built-in OpenAPI + Scalar

.NET 9 and later no longer ship Swagger UI by default. Built-in OpenAPI generation plus the free Scalar viewer gives interactive docs with few dependencies. *Alternative:* Swashbuckle, which is widely used but is no longer part of the default template; whether it suits .NET 10 should be checked before choosing it.

## Open decisions for Stage 2 (deliberately not decided)

These will be researched and verified when we reach Stage 2, then recorded here.

| Question | Options to compare |
|---|---|
| Language for AI pieces | Keep everything in C# (simpler deployment, one codebase) vs. a small Python service (richer ML ecosystem) |
| Embeddings | Local open-source model (free, needs CPU/RAM) vs. optional hosted API (paid) |
| Keyword + vector search | SQLite FTS5 plus a vector extension for local development; PostgreSQL full-text plus `pgvector` for deployment |
| LLM | Local model via Ollama (free, hardware-dependent) vs. a hosted provider behind an interface so the app is not tied to one vendor |
| Reranking | Lightweight score-fusion first; a cross-encoder model only if evaluation shows a benefit |
