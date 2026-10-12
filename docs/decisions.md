# Design Decisions and Trade-offs

Each entry says what was chosen, why, what the alternatives were, and the downside.

## D1. .NET 10 (LTS) instead of .NET 8

.NET 8 and .NET 9 both reach end of support on 10 November 2026; .NET 10 is an LTS release supported until November 2028. Starting a new portfolio project on a runtime that is about to lose support would look dated.
*Downside:* some tutorials still show .NET 8, so small differences may appear.

## D2. Controllers instead of minimal APIs

Controllers group related endpoints in a class and are what most .NET job descriptions mention. Minimal APIs are shorter but scatter endpoints. *Downside:* a little more ceremony.

## D3. SQLite now, PostgreSQL at deployment

SQLite needs no server, which suits a beginner on Windows. EF Core lets us switch providers later. *Downside:* SQLite lacks some types and features (see D5), and a SQLite file does not survive on ephemeral cloud hosts.

## D4. EF Core migrations, one set per provider

`EnsureCreated` was replaced by migrations applied at startup (`Database:AutoMigrate`). SQLite and PostgreSQL generate
different SQL and column annotations (e.g. identity columns), so each provider has its own migration set via a thin
derived context (`SqliteClaimWiseDbContext`, `PostgresClaimWiseDbContext`); the model itself is shared. A test fails if either
snapshot drifts from the model. A database created by the old `EnsureCreated` code is detected and the user is told to
delete it (it only ever held synthetic data). *Downside:* every model change needs two migrations.

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

## D11. Claim documents: metadata endpoint kept, real upload added

The metadata-only endpoint remains for "received" bookkeeping; a multipart upload now stores the real file (allow-list, content
check, GUID name on local disk, SHA-256, size). Claim-document *contents* are not used for retrieval. Local disk is a
development choice; ephemeral hosts need object storage behind `IClaimFileStore`.

## D12. API documentation: built-in OpenAPI + Scalar

.NET 9 and later no longer ship Swagger UI by default. Built-in OpenAPI generation plus the free Scalar viewer gives interactive docs with few dependencies. *Alternative:* Swashbuckle, which is widely used but is no longer part of the default template; whether it suits .NET 10 should be checked before choosing it.

## D13. RAG library in C# in its own project

Everything AI-related is C#, in `ClaimWise.Rag` with no EF Core/ASP.NET dependency: one language and one deployable, and the
pipeline is unit-testable with fakes. *Alternative:* a Python service with a richer ML ecosystem; rejected for the added
deployment and the fact that the models are reached over HTTP anyway.

## D14. In-memory BM25 + brute-force vectors instead of database features

Keeps keyword and vector search identical on SQLite and PostgreSQL and needs no extensions. Fine for policy corpora of
thousands of passages; beyond that use PostgreSQL full-text + pgvector (the `PolicyIndex`/`HybridRetriever` seam is where it
would change).

## D15. Offline-first providers; honest labels

The default embedder (feature hashing + small synonym table) and summary writer (extractive quoting) need no keys and are
labelled as such in the UI. Hosted models are optional adapters. The default embedder is **not** neural; this is stated in the
UI status card, README and docs/rag.md.

## D16. Abstain before generating; validate after generating

Weak evidence never reaches a model, and nothing a model writes is shown unless every citation verifies against retrieved
text. A failed or fully-invalid generation is an abstention/failure, never a fabricated answer.

## D17. Same-family conflict detection

Passages are compared for contradictions only within one document or one policy family (e.g. `gold-plan` 2024 vs its 2025
amendment). Gold vs Basic limits are different products; the summary warns that several policies matched and the reviewer can
scope the search to chosen documents.

## D18. A human review gate in the workflow, not just in the UI

The API refuses to approve/reject a claim while an Answered AI summary awaits review, and records which AI summaries a person
reviewed in the decision's audit entry. *Trade-off:* a reviewer must click accept/reject even for a summary they ignore.

## D19. No authentication yet

Unchanged from D10: reviewer names are self-declared. Documented prominently; the API must stay on a trusted network.
