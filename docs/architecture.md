# ClaimWise AI: Architecture

Diagrams are Mermaid; GitHub and the VS Code Markdown preview (with a Mermaid extension) render them. For retrieval and grounding details see [rag.md](rag.md); for endpoints see [api.md](api.md).

## 1. System context

```mermaid
flowchart LR
    R[Claims reviewer] -->|browser| FE[React + TypeScript UI]
    FE -->|HTTP JSON| API[ASP.NET Core Web API]
    API --> DB[(SQLite or PostgreSQL)]
    API --> FS[(claim-files on disk)]
    API --> RAG[ClaimWise.Rag library]
    RAG -.->|optional| EMB[Embedding provider<br/>OpenAI-compatible]
    RAG -.->|optional| LLM[Language model<br/>OpenAI-compatible or Anthropic]
```

## 2. Components

```mermaid
flowchart TD
    C[Controllers<br/>Claims, Policies, Ai] --> S[Services<br/>ClaimService, ClaimDocumentService,<br/>PolicyService, AiSummaryService]
    S --> D[Domain rules<br/>ClaimRules, ClaimWorkflow]
    S --> X[EF Core DbContext<br/>Sqlite / Postgres contexts + migrations]
    S --> G[GroundedSummaryPipeline]
    S --> H[HybridRetriever + PolicyIndexCache]
    G --> V[CitationValidator, EvidenceAssessor, ConflictDetector]
    G --> Gen[ISummaryGenerator<br/>Extractive or LlmSummaryGenerator]
    Gen --> P[ILlmProvider]
    H --> E[IEmbeddingProvider, IReranker]
    X --> DB[(database)]
    EH[ApiExceptionHandler] -. maps errors .-> C
```

* **Controllers**: HTTP only. **Services**: use cases, transactions, audit entries. **Domain**: pure rules, no framework.
* **ClaimWise.Rag**: no EF Core or ASP.NET dependency; every provider sits behind an interface so it can be replaced or faked.
* The DbContext is provider-neutral; `SqliteClaimWiseDbContext` / `PostgresClaimWiseDbContext` exist so each provider owns its
  migrations (see decisions D4).

## 3. Request flow: changing a claim's status

```mermaid
sequenceDiagram
    participant U as Reviewer
    participant C as ClaimsController
    participant S as ClaimService
    participant R as Domain rules
    participant DB as SQLite
    U->>C: POST /api/claims/5/status
    C->>C: Validate body (reviewer, note)
    C->>S: ChangeStatusAsync
    S->>DB: Load claim + documents
    S->>R: CanTransition(from, to)?
    alt Approve
        S->>R: Validate completeness
    end
    S->>DB: Save status + audit entry (one save)
    S-->>C: Updated claim
    C-->>U: 200 OK
    Note over S,U: Business rule failures throw an exception that becomes 409 Conflict
```

## 4. Database ER diagram

```mermaid
erDiagram
    CLAIM ||--o{ CLAIM_DOCUMENT : has
    CLAIM ||--o{ AUDIT_ENTRY : records
    CLAIM ||--o{ AI_SUMMARY : has
    AI_SUMMARY ||--o{ AI_SUMMARY_STATEMENT : contains
    AI_SUMMARY ||--o{ AI_SUMMARY_PASSAGE : "snapshots"
    POLICY_DOCUMENT ||--o{ POLICY_CHUNK : "split into"
    CLAIM {
        int Id PK
        string ClaimNumber UK
        string PatientName
        string MemberId
        string PolicyNumber
        string ProviderName
        string ClaimType
        string DiagnosisCode
        string ProcedureCode
        double AmountClaimed
        date ServiceDate
        string Status
        datetime SubmittedAt
        datetime UpdatedAt
        int Version "concurrency token"
        string ReviewerName
        string ReviewerNote
    }
    CLAIM_DOCUMENT {
        int Id PK
        int ClaimId FK
        string DocumentType
        string FileName
        datetime ReceivedAt
    }
    AI_SUMMARY {
        int Id PK
        int ClaimId FK
        string Status "Answered | Abstained | Failed"
        string ReviewState "NotRequired | PendingReview | Accepted | Rejected"
        string ReviewedBy
        string AssessmentJson
    }
    AI_SUMMARY_STATEMENT {
        int Id PK
        string Text
        string Origin "Model | System"
        string CitationsJson
    }
    AI_SUMMARY_PASSAGE {
        int Id PK
        string Label
        string Text "snapshot"
    }
    POLICY_DOCUMENT {
        int Id PK
        string Title
        string PolicyFamily
        string Version
        string Sha256 UK
        bool IsActive
    }
    POLICY_CHUNK {
        int Id PK
        string ClauseRef
        string SectionPath
        int PageNumber
        bytes Embedding
    }
    AUDIT_ENTRY {
        int Id PK
        int ClaimId FK
        string Action
        string Details
        string Actor
        datetime Timestamp
    }
```

## 5. API endpoints

See [api.md](api.md) for the complete list. Interactive documentation: `/scalar/v1` in Development.

## 6. Error handling

| Situation | Response |
|---|---|
| Bad input | 400, standard validation problem JSON with field names |
| Unknown claim | 404 problem JSON |
| Business rule violated | 409 problem JSON with a readable explanation |
| Anything unexpected | 500 generic problem JSON; details go to logs only |

## 7. Security considerations

Implemented: server-side validation, whitelisted sort fields, parameterized queries, upload allow-list + content checks +
server-generated storage names, XML hardening, security headers, rate limiting on upload/AI endpoints, CORS restricted to
configured origins, secrets/databases/uploads excluded from Git, sanitised provider errors, prompts and keys never logged.
**Not implemented:** authentication, authorization, HTTPS enforcement, audit-log tamper protection (all required before any
real deployment).

## 8. AI architecture

Implemented; see [rag.md](rag.md) for the pipeline diagram, scoring, validation rules and limitations.

## 9. Deployment (not done)

Frontend on static hosting, API on a container host, PostgreSQL for persistence (SQLite files and `claim-files/` are lost on
ephemeral hosts). Not set up or verified here.
