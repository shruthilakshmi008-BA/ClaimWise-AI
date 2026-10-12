# Retrieval and grounded summaries: how it works, and what it does not do

Code: `src/ClaimWise.Rag` (framework-free library) and `src/ClaimWise.Api/{Ai,Services}`.
Everything below describes what is implemented and tested; limitations are stated in section 8.

## 1. Pipeline

```
upload ─► extract text ─► chunk (clause-aware, metadata) ─► embed ─► store (EF Core)
                                                                         │
question + claim ─► query ─► BM25 keyword ┐                              ▼
                             vector cosine ┴► RRF fusion ─► rerank ─► top passages
                                                                         │
                         evidence sufficiency ──► insufficient? ──► ABSTAIN (generator is never called)
                                 │ sufficient
                      conflict detection (same policy family only)
                                 │
                       generator drafts statements ─► citation validation ─► drop what fails
                                 │
              + system conflict notices (verbatim, both sides) ─► stored as PendingReview ─► human accepts/rejects
```

## 2. Ingestion

* **Extraction**: `.txt/.md` (UTF-8, binary refused), `.pdf` (PdfPig, per page; image-only/scanned PDFs are refused —
  there is no OCR), `.docx` (zip + XML, heading styles kept; DTD processing off, size capped).
* **Chunking** (`PolicyChunker`): detects Markdown, numbered (`4.2 Title`) and ALL-CAPS headings; every numbered clause
  (`2.1 …`) becomes its own chunk so a citation points at one clause; long clauses are split on sentence boundaries with an
  overlap; a chunk never crosses a page break.
* **Metadata kept per chunk**: document id/title/version/policy family, clause reference, heading, section path, page
  number, start/end offsets into the extracted text, word count, embedding model id. The original file is not stored.

## 3. Retrieval

* **Keyword**: Okapi BM25 (k1 1.2, b 0.75) implemented in code over an in-memory index, so it behaves identically on
  SQLite and PostgreSQL. Terms are lower-cased, stop-word filtered and lightly stemmed; codes such as `PRC-101` or `J06.9`
  stay whole tokens.
* **Vector**: cosine similarity over stored embeddings (`IEmbeddingProvider`). Brute force, which is fine for policy
  corpora of thousands of passages; use pgvector or an ANN index beyond that.
* **Fusion**: Reciprocal Rank Fusion (k 60) because BM25 and cosine scores are not comparable.
* **Rerank** (`HeuristicReranker`): score in [0,1] from idf-weighted question-term coverage (one group per word, so
  "co-payment" counts once), matched adjacent term pairs, exact code matches, how well the section heading / clause title
  explains the question, and the fused score as a weak prior.
* Every returned passage carries its keyword/vector scores and ranks, fused score and rerank score.

## 4. Providers (abstractions)

| Concern | Interface | Implementations |
|---|---|---|
| Embeddings | `IEmbeddingProvider` | `HashingEmbeddingProvider` (local, offline, deterministic — **not a neural model**); `OpenAiCompatibleEmbeddingProvider` (any `/embeddings` endpoint) |
| Language model | `ILlmProvider` | `OpenAiCompatibleLlmProvider` (OpenAI, Ollama, vLLM, …); `AnthropicLlmProvider` (official SDK) |
| Summary writer | `ISummaryGenerator` | `LlmSummaryGenerator` (any `ILlmProvider`); `ExtractiveSummaryGenerator` (no model: quotes the best sentence of each passage) |
| Reranker | `IReranker` | `HeuristicReranker` (a cross-encoder or LLM reranker would implement the same interface) |

HTTP providers have a per-attempt timeout, bounded retries with backoff for 408/429/5xx, no retry on 401/403, and never log
prompts or keys. Provider failures surface as `Failed` summaries (or 502/503 on upload/search), never as invented content.

## 5. Evidence sufficiency and abstention

`EvidenceAssessor` requires **all** of: best rerank score ≥ `MinTopScore` (0.35), at least `MinSupportingPassages` (1)
passage ≥ `SupportThreshold` (0.30), and supporting passages that together contain ≥ `MinCoverage` (50 %) of the question's
idf-weighted terms. Code-like terms are excluded from coverage. When it fails, the pipeline **abstains before any generator
runs** and states why (including which question terms were not found). Thresholds are configuration (`Ai:Evidence`) and
are heuristics calibrated on the sample corpus, not statistically validated values.

## 6. Citation validation (`CitationValidator`)

A generated statement is kept only if **all** hold: it has ≥ 1 citation; every citation names a passage that was actually
retrieved; every quote (≥ 3 words) appears **verbatim** in that passage (case/whitespace/punctuation-insensitive); every
number in the statement appears in the cited text or the claim facts (compared numerically); ≥ 30 % of its wording
appears in the cited passages; and it does not recommend or announce a claim decision. Failing statements are removed and
listed (with reasons) on the summary; citations are never repaired or invented. If nothing survives, the result is an
abstention.

## 7. Conflicts and human review

* `ConflictDetector` flags **value conflicts** (near-identical wording, different percentage/duration/amount) and
  **coverage conflicts** (one passage grants cover, another excludes it) between passages of the same document or the same
  policy family. Different plans (Gold vs Basic) are not "conflicts"; the summary warns that evidence comes from several
  policies. The system never picks a side: it appends a system-written notice quoting both passages and the reviewer resolves
  it.
* A summary with findings is stored `PendingReview`. A claim cannot be **Approved or Rejected** while one is pending; a
  named person accepts or rejects it (note required to reject). Generation and review are written to the claim's audit trail,
  and the decision's audit entry lists the AI summaries a person reviewed first. The AI has no code path that changes a
  claim's status.

## 8. Limitations (read before relying on this)

* The default embedder is feature hashing with a small hand-written synonym table; it gives typo/word-form tolerance and a
  few domain synonyms, **not** semantic understanding. Configure an OpenAI-compatible embedding model for that — it was not
  exercised live here (no credentials), only through stubbed HTTP tests.
* The reranker is a feature-based heuristic, not a cross-encoder.
* The Anthropic and OpenAI-compatible **language-model** summaries were **never run against a live model** in this work
  (no API key); the HTTP/SDK handling, prompt construction, JSON parsing and validation are tested with scripted/stubbed
  providers. Treat live-model quality as untested.
* Conflict detection and quantity extraction are regex heuristics: they can miss conflicts (different phrasing) and can
  flag false ones; they exist to alert a human, not to adjudicate.
* Prompt-injection mitigation is layered, not absolute: untrusted text is placed in delimited data blocks (delimiter
  forgery stripped), the model is told to treat it as data, and the output is structurally validated so injected text cannot
  produce uncited content or a decision — but a model could still be steered into a misleading *cited* sentence, which is
  why a human must review every summary.
* The in-memory index is rebuilt after changes and holds all active chunks; it targets small corpora.
* No OCR; no multi-language handling; claim-document contents are not used for retrieval (only structured claim fields and
  the reviewer's question).

## 9. Evaluation

`tests/ClaimWise.Tests/Rag/RetrievalTests.cs` contains 16 hand-written questions over the four fictional sample policies,
each with the acceptable (document, clause) answers. Measured with the committed code:

| Method | recall@3 | MRR |
|---|---|---|
| Keyword only (BM25) | 0.938 | 0.896 |
| Vector only (hashing embedder) | 1.000 | 0.927 |
| **Hybrid + rerank** | **1.000** | **0.917** |

The set is tiny, written by the same author who tuned the stemmer/synonyms/reranker against it, and the corpus is small, so
these numbers are an optimistic regression guard — **not** a benchmark. The test asserts only that hybrid is not materially
worse than either single ranker. Additional behaviour tests cover abstention (off-topic questions), conflicts, citation
fabrication, and two regressions found by running the real workflow in a browser (a procedure code drowning the question;
folded-synonym idf).
