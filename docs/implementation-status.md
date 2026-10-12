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
| A-01 | P0 | Feature | No policy document upload, extraction, chunking, retrieval, LLM/embedding abstraction, summaries, citations or abstention (Stage 2 entirely missing) | Implemented (sections 3–4) |
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

## 3. Commits (branch `claude/claims-review-audit-complete-j6ijc1`, oldest first)

Baseline = `14ac600` (existing work, untouched history). Nothing was amended, rebased or pushed.

| Hash | Message | Verified by |
|---|---|---|
| `179df12` | docs(status): record baseline build/test results and audit findings | – |
| `e123b6b` | fix(config): do not seed demo claims outside Development | tests 51/51 |
| `aaa6dd5` | feat(api): add baseline security response headers | test |
| `73e4c10` | fix(claims): optimistic concurrency, stale notes, validation and search gaps | 7 new regression tests |
| `eb6da7b` | feat(db): replace EnsureCreated with EF Core migrations and add PostgreSQL | tests 54/54 + run on PostgreSQL 16 |
| `c8d6504` | feat(documents): add text extraction and structure-aware policy chunking | tests 67/67 |
| `7e72ffe` | feat(rag): implement hybrid policy evidence retrieval | tests 79/79 + evaluation set |
| `d418c20` | feat(ai): validate citations, assess evidence and handle conflicts and abstention | tests 124/124 |
| `b9ea1f0` | fix(documents): never let a chunk span a page break | test (found via PDF upload) |
| `23b1526` | feat(documents): add policy upload, ingestion and hybrid search API | tests 136/136 |
| `329bf01` | feat(ai): persist cited claim summaries and require human review before decisions | tests 149/149 (full workflow over HTTP) |
| `a468ea5` | feat(claims): accept real claim document uploads with hardened storage | tests 156/156 |
| `555dfc4` | feat(web): add React + TypeScript reviewer UI connected to the real API | tsc, build, vitest 44/44 |
| `1bd636a` | feat(documents): ingest the fictional sample policies on first Development start | run: 4 documents / 53 passages ingested |
| `c66c127` | fix(rag): weight reranking and coverage over the folded vocabulary, count compounds once | found by browser e2e; 161/161 |
| `d4c11f2` | fix(ai): do not let the claim's procedure code drown the reviewer's question | found by browser e2e; regression tests |
| `83e4fb8` | fix(ui): tidy system-notice numbering and audit wording | found via screenshots |
| `06df582` | test(e2e): add a browser check of the main reviewer workflow | 25/25 on a fresh stack |
| `a058144` | docs(api): document the API and the retrieval/grounding design with its limits | – |
| `1b28575` | docs(setup): rewrite README, add configuration templates and testing guide | – |
| `10fe1e4` | docs(architecture): replace planned-work text with the implemented design | – |

(The commit that adds this final version of the file cannot list its own hash.)

## 4. Verified results

Run in a **clean clone of `10fe1e4`** (so nothing depends on untracked files):

| Check | Result |
|---|---|
| `dotnet restore && dotnet build` | succeeded, **0 warnings, 0 errors** |
| `dotnet test` | **161 passed, 0 failed** |
| `cd web && npm ci && npm run build` | `tsc --noEmit` clean; production build OK (303 kB JS / 13 kB CSS) |
| `cd web && npm test` | **44 passed** (8 files) |
| Browser e2e (`e2e/run.mjs`, headless Chromium, real API + Vite, **fresh SQLite DB**) | **25/25 checks** |
| API + full workflow on **PostgreSQL 16** (4 migrations applied; `numeric` money, `bytea` embeddings) | claim → documents → in review → cited summary (1 conflict) → approval blocked (409) → human review → approval → audit order; verified with a script against the running API |

### End-to-end workflows verified

1. **Main workflow (browser, SQLite):** create claim (client + server validation) → upload 3 required documents (real files) →
   completeness turns green → move to In review as a named reviewer → ask for policy evidence → cited summary with a conflict
   banner (5,000 vs 6,000, 2024 policy vs 2025 amendment), mixed-plan warning, verbatim citation highlighted → Approve disabled
   → accept summary as a named reviewer → approve with a note → audit shows generation, review and decision in order and
   records that the AI summary was reviewed by a person.
2. **Abstention:** an off-topic question returns "Insufficient evidence — no findings shown" with the reason and missing
   terms; no statements; no review needed.
3. **Same workflow over HTTP on PostgreSQL** (see table).
4. **Backend HTTP integration tests** cover the same flow plus: fabricated citations (dropped → abstention), decision language
   (refused), provider failure (recorded as Failed, no content), policy scoping, mixed plans, final claims, double review.
5. List search/filter/sort/pagination, policy upload/search/deactivate, mobile width (390 px, no horizontal scroll).

### Defects found *by running the real workflow* (not by the unit tests that existed) and fixed

* Chunks that spanned a page break reported the wrong page (`b9ea1f0`).
* Reranker/sufficiency idf was looked up on an unfolded vocabulary and compound words were counted twice, so the outpatient
  limit clause fell out of the results for "annual outpatient limit and co-payment" (`c66c127`).
* The claim's procedure code appended to every query drowned the reviewer's question (`d4c11f2`).
* Uploads in tests went to the default folder because configuration added after registration was ignored (fixed with lazy
  options in `a468ea5`).
* A number-comparison bug ("1850.00" ≠ "1850") in citation validation (caught by a test, fixed in `d418c20`).

### Retrieval evaluation (measured, small, author-written — see docs/rag.md §9)

Hybrid recall@3 = 1.000, MRR = 0.917 (keyword-only 0.938 / 0.896; vector-only 1.000 / 0.927) over 16 questions.

## 5. Not verified / limitations / blocked

* **No live language-model or hosted-embedding calls were made** (no credentials in this environment). The Anthropic SDK
  provider compiles and its error mapping is written from the SDK's documented exception types, but it was never called; the
  OpenAI-compatible providers are covered by stubbed-HTTP tests (retry, timeout, auth, malformed responses) only. Quality of
  real-model summaries is therefore **unmeasured**; the validators are what protect the output.
* The default embedder is feature hashing (not neural) and the reranker is heuristic; the 16-question evaluation is too
  small to support any general accuracy claim, and it was used while tuning.
* Conflict detection and sufficiency thresholds are heuristics calibrated on the sample policies.
* **No authentication/authorization**; reviewer names are self-declared; no tamper-proof audit; HTTPS not enforced.
* No OCR; the in-memory index targets small corpora; uploaded claim files live on local disk.
* SQLite stores money as a double (PostgreSQL uses `numeric`).
* PostgreSQL was verified manually (a local PG 16 server), not as part of the automated test suite; the automated tests check
  that the PostgreSQL model matches its migration snapshot.
* The e2e script is a verification tool that needs a manually started stack; there is **no CI**, and nothing was deployed.
* The sandbox blocked the official .NET download host; the SDK was installed from the Ubuntu archive (10.0.112).
* Not pushed: all work is committed locally on the branch above.
