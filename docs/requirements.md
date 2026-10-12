# ClaimWise AI: Requirements (Stage 1 MVP)

All business rules here are **fictional** and exist to demonstrate a realistic workflow. They are not real insurer policy.

## 1. Problem statement

Claims reviewers receive claims with varying levels of completeness. Checking which documents and details are missing, tracking where each claim stands, and recording who decided what is done by hand, which is slow, inconsistent and hard to audit.

## 2. Business objectives

1. Make incomplete submissions visible immediately instead of during review.
2. Give every claim one clear status and a controlled path through review.
3. Keep a trustworthy history of every change.
4. Make sure a person, not software, makes every approval or rejection.

## 3. Stakeholders and personas

| Stakeholder | Interest |
|---|---|
| **Claims reviewer** (primary user) | Work through claims quickly; know what is missing; record decisions |
| **Intake coordinator** | Log new claims and received documents accurately |
| **Claims supervisor** | See workload and incomplete claims; review the audit trail |
| **Compliance / audit** | Evidence of who changed what and when |

**Persona: Maya, claims reviewer.** Handles claims all day. Her frustration is opening a claim and discovering, ten minutes in, that a document is missing. She wants that visible in the list view.

## 4. User stories

| ID | Story |
|---|---|
| US-1 | As an intake coordinator, I enter a claim so that it enters the review queue with a unique claim number. |
| US-2 | As a reviewer, I search, filter, sort and page through claims so that I can find the right one fast. |
| US-3 | As a reviewer, I see which documents and fields are missing so that I can request them before reviewing. |
| US-4 | As an intake coordinator, I record a received document so that the completeness check updates. |
| US-5 | As a reviewer, I move a claim through the workflow with a note so that its state is always clear. |
| US-6 | As a reviewer, I am prevented from approving an incomplete claim so that mistakes are caught early. |
| US-7 | As a supervisor, I see counts per status and how many open claims are incomplete. |
| US-8 | As an auditor, I see a time-ordered history of every change to a claim. |

## 5. Functional requirements

| ID | Requirement | Why it matters | Story |
|---|---|---|---|
| FR-1 | Create a claim with patient, member ID, policy number, provider, type, codes, amount and service date; generate a claim number `CW-000123` | Unique identifier for conversation and audit | US-1 |
| FR-2 | Validate input on the server and return field-level errors | Bad data must not enter the system, whatever the client | US-1 |
| FR-3 | List claims with filter (status, type), text search, sort, pagination | Reviewers handle many claims | US-2 |
| FR-4 | Show the required documents and fields for a claim type, and which are missing | Core value: surface incompleteness early | US-3 |
| FR-5 | Record received documents (metadata); reject duplicates of the same type | Keeps the completeness check honest | US-4 |
| FR-6 | Enforce allowed status transitions (section 7) | Prevents skipping review | US-5 |
| FR-7 | Require reviewer name; require a note for Approved, Rejected, NeedsInformation | Accountability for every decision | US-5 |
| FR-8 | Refuse approval while documents or required fields are missing | Business rule BR-4 | US-6 |
| FR-9 | Dashboard summary: counts by status, total claimed, incomplete open claims | Workload visibility | US-7 |
| FR-10 | Record an audit entry for creation, each document and each status change | Traceability | US-8 |
| FR-11 | Final claims (Approved/Rejected) cannot receive new documents or change status | Protects decided cases | US-5 |

## 6. Non-functional requirements

| ID | Requirement |
|---|---|
| NFR-1 | Errors use one consistent JSON format; internal details are never exposed to callers |
| NFR-2 | All input is validated server-side; database access is parameterized |
| NFR-3 | Business rules live in code that can be unit tested without a database |
| NFR-4 | Runs locally on Windows with free tools only |
| NFR-5 | Structured logging for creation and status changes |
| NFR-6 | Synthetic data only; no claim of regulatory compliance |
| NFR-7 | The API is documented interactively |

## 7. Business rules

**BR-1: Required documents by claim type**

| Claim type | Required documents |
|---|---|
| Outpatient | ClaimForm, Invoice, MedicalReport |
| Inpatient | ClaimForm, Invoice, MedicalReport, DischargeSummary |
| Pharmacy | ClaimForm, Invoice, Prescription |
| Dental | ClaimForm, Invoice |

**BR-2: Required fields.** `DiagnosisCode` is required for Outpatient and Inpatient. `ProcedureCode` is required for everything except Pharmacy.

**BR-3: Allowed status changes**

| From | To |
|---|---|
| Submitted | InReview, NeedsInformation |
| InReview | NeedsInformation, Approved, Rejected |
| NeedsInformation | InReview |
| Approved / Rejected | none (final) |

**BR-4:** A claim cannot be Approved unless BR-1 and BR-2 are satisfied. (Rejection is allowed at any time during review.)

**BR-5:** The service date cannot be in the future; the claimed amount must be between 0.01 and 10,000,000.

## 8. Assumptions and constraints

- Single organization, no multi-tenancy.
- Currency is not modelled; amounts are plain numbers.
- Documents are tracked as metadata only in Stage 1.
- No login yet, so the reviewer name is self-declared.
- Budget: free and open-source tools only.

## 9. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Self-declared reviewer weakens the audit trail | Documented limitation; authentication added before deployment |
| Someone enters real patient data | Prominent synthetic-data notice; no deployment with real data |
| SQLite money handling imprecise | Documented; PostgreSQL numeric type at deployment |
| Later AI output mistaken for a decision | Human decision rule; AI labeled; abstain when evidence is weak (Stage 2) |

## 10. Success metrics (how we will judge Stage 1)

No numbers are claimed. Stage 1 is successful when:

- All automated tests pass on a clean Windows machine.
- Every business rule BR-1 to BR-5 has at least one test.
- A reviewer can complete the flow *create → add documents → review → decide* and the audit history shows each step.

## 11. Scope

**Stage 1 (FR-1 to FR-11)**: implemented. **Added since:** claim-document upload; policy upload, extraction and chunking with
source metadata; hybrid retrieval with reranking; evidence-grounded summaries with validated citations, evidence-sufficiency
abstention and conflict handling; human review of AI summaries (a gate before decisions) with audit; PostgreSQL + migrations;
React UI; automated tests. See [implementation-status.md](implementation-status.md).

| ID | Requirement (Stage 2) |
|---|---|
| FR-12 | Upload policy documents (.txt .md .pdf .docx), extract text, chunk with clause/section/page metadata |
| FR-13 | Hybrid retrieval (keyword + vector) with reranking; expose per-stage scores |
| FR-14 | Summaries cite real passages; every quote verified verbatim; figures grounded; no decision language |
| FR-15 | Abstain (without calling a model) when evidence is insufficient; explain why |
| FR-16 | Detect and surface conflicting passages without resolving them |
| FR-17 | AI never changes claim status; a person accepts/rejects each summary with findings before the claim can be approved/rejected |
| FR-18 | Audit AI generation, human review and AI-aware decisions |

**Still out:** authentication/roles, deployment, CI, OCR, notification simulation.

## 12. Future enhancements

Role-based access and verified identities; a cross-encoder or LLM reranker; neural embeddings by default with pgvector;
a larger labelled retrieval evaluation set; OCR for scanned policies; object storage for uploads; CI pipeline.
