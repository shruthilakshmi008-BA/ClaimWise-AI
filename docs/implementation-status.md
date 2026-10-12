# Implementation Status

This file is the running record of the audit-and-completion work: what was found, what was changed, which commit did it, and how it was verified. Statements here are limited to what was actually executed; anything not executed is listed under "Not verified".

## 1. Baseline (before any change)

Environment: Ubuntu 24.04 container, .NET SDK 10.0.112 (installed from the Ubuntu archive because `builds.dotnet.microsoft.com` is blocked by the sandbox egress policy), Node 22.22.

| Step | Result |
|---|---|
| `dotnet restore` | succeeded |
| `dotnet build` | succeeded, 0 warnings, 0 errors |
| `dotnet test` | **44 passed, 0 failed** (29 unit + 15 HTTP integration) |
| Frontend | none existed |
| README / progress.md claim | "never built by the author" — outdated: the code builds and its tests pass |

## 2. Audit findings

Priority: **P0** blocks required behaviour, **P1** correctness/security defect, **P2** quality gap.

| ID | Pri | Area | Finding | Resolution |
|---|---|---|---|---|
| A-01 | P0 | Feature | No policy document upload, extraction, chunking, retrieval, LLM/embedding abstraction, summaries, citations or abstention (Stage 2 entirely missing) | Implemented (see section 4) |
| A-02 | P0 | Feature | No frontend | Implemented (React + TypeScript + Vite) |
| A-03 | P0 | Feature | Claim documents are metadata only; nothing can be uploaded | Multipart upload added; metadata endpoint kept |
| A-04 | P1 | Data | `EnsureCreated` is used; schema cannot evolve, no migrations | EF Core migrations added |
| A-05 | P1 | Data | No PostgreSQL support although the architecture promises it | Npgsql provider + separate migration set |
| A-06 | P1 | Correctness | No concurrency control: two reviewers changing the same claim silently overwrite each other | Concurrency token + 409 |
| A-07 | P1 | Correctness | `ReviewerNote` from an earlier status change is retained and shown next to the *new* reviewer's name | Note now replaced on each change |
| A-08 | P1 | Security/ops | `Seed:Enabled=true` in the base `appsettings.json`, so production would load demo claims | Default off; enabled only in Development settings |
| A-09 | P1 | Correctness | `CreateClaimRequest` future-date check uses `DateTime.UtcNow`, bypassing the injected `TimeProvider` (untestable, inconsistent) | Uses `TimeProvider` |
| A-10 | P2 | Validation | Amounts with more than 2 decimals are accepted | Validated |
| A-11 | P2 | Security | No security response headers, no rate limiting, no upload limits | Added where relevant |
| A-12 | P2 | Feature | Sorting by status not available; search does not cover policy number or codes | Added |
| A-13 | P2 | Docs | README/progress describe a state that no longer matches reality | Rewritten |

## 3. Commits

(Filled in at the end of the work; see section 5.)

## 4. Feature status

(Filled in as units are completed.)

## 5. Not verified / limitations

(Filled in at the end.)
