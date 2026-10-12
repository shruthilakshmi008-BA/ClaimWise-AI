# API reference

Interactive documentation (OpenAPI + Scalar) is available while running in Development at **`/scalar/v1`**; the raw
OpenAPI document is at `/openapi/v1.json`. This page is the human-readable summary. All bodies are JSON unless
stated; enums are strings; dates are ISO 8601. There is **no authentication yet** (see README → Security).

## Error format

Every error is RFC 7807 problem JSON: `{ "title", "status", "detail" }`; validation errors (400) also carry
`errors: { Field: ["message"] }`. Internal details never reach the caller.

| Status | Meaning |
|---|---|
| 400 | Validation failed (field-level messages) |
| 404 | Claim, document or summary not found |
| 409 | A business rule refused the request (invalid status move, incomplete claim, unreviewed AI summary, stale `expectedVersion`, duplicate upload) |
| 413 | Upload larger than the limit |
| 415 | Unsupported or mislabelled file type |
| 422 | A policy document could not be read (binary text, scanned PDF, corrupt file) |
| 429 | Rate limit on upload/AI endpoints |
| 502 / 503 | The embedding or language-model provider failed (non-transient / transient) |

## Claims

| Method | Path | Notes |
|---|---|---|
| POST | `/api/claims` | Create. `patientName, memberId, policyNumber, providerName, claimType, diagnosisCode?, procedureCode?, amountClaimed (0.01–10,000,000, ≤2 decimals), serviceDate` → 201 |
| GET | `/api/claims` | Query: `status, claimType, search, sortBy (submittedAt\|serviceDate\|amount\|claimNumber\|status), sortDir, page, pageSize (≤100)`. Search covers claim no., patient, member, policy, provider, diagnosis and procedure code |
| GET | `/api/claims/summary` | Counts per status, total claimed, open claims with an incomplete submission |
| GET | `/api/claims/{id}` | Detail incl. `version`, documents, `validation` (missing documents/fields) and `allowedNextStatuses` |
| GET | `/api/claims/{id}/validation` | Completeness only |
| POST | `/api/claims/{id}/documents` | Record that a document was received (metadata only): `{documentType, fileName}` |
| POST | `/api/claims/{id}/documents/upload` | `multipart/form-data`: `DocumentType`, `File` (pdf, png, jpg, docx, txt, ≤10 MB, content must match extension) |
| GET | `/api/claims/{id}/documents/{documentId}/file` | Download (always an attachment) |
| POST | `/api/claims/{id}/status` | `{newStatus, reviewerName, note?, expectedVersion?}`. A note is required for Approved/Rejected/NeedsInformation. 409 if the move is not allowed, the claim is incomplete (approve), an AI summary is awaiting review (approve/reject) or `expectedVersion` is stale |
| GET | `/api/claims/{id}/audit` | Time-ordered history: creation, documents, status changes, AI summary generation and review |

Status workflow: `Submitted → InReview \| NeedsInformation`, `InReview → NeedsInformation \| Approved \| Rejected`,
`NeedsInformation → InReview`; Approved/Rejected are final.

## AI summaries (per claim)

| Method | Path | Notes |
|---|---|---|
| POST | `/api/claims/{id}/summaries` | `{question?, requestedBy, documentIds?}` → **201** with an `AiSummary` whose `status` is `Answered`, `Abstained` or `Failed`. It never changes the claim status. 409 for final claims |
| GET | `/api/claims/{id}/summaries` | Newest first |
| GET | `/api/claims/{id}/summaries/{summaryId}` | |
| POST | `/api/claims/{id}/summaries/{summaryId}/review` | `{decision: Accepted\|Rejected, reviewerName, note?}` (note required to reject). One verdict per summary; summaries without findings need none |

An `AiSummary` contains: `statements[]` (each with `citations[{passage, quote}]` and `origin` = `Model` or `System`),
`passages[]` (the exact text snapshot the summary saw, with document, version, clause, page, scores), `evidence`
(verdict, reasons, coverage, missing terms), `conflicts[]`, `warnings[]`, `rejectedStatements[]` (generated statements that
failed citation checks, with reasons), `abstainReason` / `failureReason`, `review`, and the generator/model used.

## Policy documents and search

| Method | Path | Notes |
|---|---|---|
| POST | `/api/policies` | `multipart/form-data`: `File` (.txt .md .pdf .docx ≤10 MB), `UploadedBy`, `Title?`, `PolicyFamily?`, `Version?`, `EffectiveDate?`. Duplicate content → 409 |
| GET | `/api/policies` | All documents (incl. inactive) |
| GET | `/api/policies/{id}` · `/chunks` | Document; its passages with clause, section path, page, offsets |
| PATCH | `/api/policies/{id}/active` | `{isActive}`; inactive documents are excluded from retrieval |
| POST | `/api/policies/reindex` | Re-embed everything with the configured embedding model |
| POST | `/api/policies/search` | `{query (3–500), topK (1–20), mode: Hybrid\|KeywordOnly\|VectorOnly, documentIds?}` → passages with `rerankScore, fusedScore, keywordScore/Rank, vectorScore/Rank` |
| GET | `/api/ai/status` | Active providers, indexed documents/passages, stale-embedding count |
| GET | `/health` | Liveness |
