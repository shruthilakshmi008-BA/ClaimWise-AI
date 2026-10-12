# Reuse Assessment: existing BA work → ClaimWise AI

## What was actually inspected

| Resource | Access | What I could read |
|---|---|---|
| GitHub profile `shruthilakshmi008-BA` | Yes | One public repository: `ba-automation-suite` |
| `ba-automation-suite` main page and README | Yes | The README text, folder names and the architecture principles |
| Files **inside** each module folder (`01` to `06`) | **No** | GitHub blocked automated access to subfolders |
| BA portfolio site | Yes | Full public page text |

Because I could not open the module folders, **I have not read any of your source code**. Everything below is based on the README descriptions and your portfolio page. To get a code-level assessment, send the ZIP (or the files) for the modules you want to reuse, starting with `04. Invoice-Extractor` and `06-Enterprise-O2C-Pipeline`.

## What the README tells us

The suite has six folders: Status-cleanser, Requirement-Extractor, User-Story-Generator, Invoice-Extractor, KPI-Dashboard-Generator and Enterprise-O2C-Pipeline. They are Python / Jupyter Notebook work. The README states three design principles that fit ClaimWise well:

1. **Rule-first, deterministic logic** handles standard inputs; an LLM is a fallback.
2. **Generative AI fallback** for unstructured or out-of-spec input.
3. **Auditable exception handling:** low-confidence results are flagged `NEEDS_REVIEW` instead of guessed.

## What to reuse

| Item | Reuse type | How it applies |
|---|---|---|
| The "flag for review, never guess" principle | **Design idea** (already applied) | Directly matches ClaimWise's rule that a human decides and the AI must abstain when evidence is weak |
| Rule-first, LLM-second pattern | **Design idea** (Stage 2) | Cheap deterministic checks (missing documents/fields) run first; retrieval and the LLM only add value on top |
| Hybrid document-processing approach from `04. Invoice-Extractor` | **Concept, possibly code** (Stage 2) | Likely relevant to extracting text and fields from uploaded documents. Needs a code review before deciding |
| Requirements and user-story habits from modules 02 and 03 | **Process** | Used for `docs/requirements.md` |
| Healthcare domain experience on your portfolio (practice-management workflows, HIPAA-governed data) | **Domain knowledge** | Use it to sanity-check the fictional business rules, without copying anything confidential |

## What to rewrite, not port

- **Python notebooks → C# services.** ClaimWise's backend is C#. Porting notebook code line by line would give messy results; reimplement the *logic* cleanly instead. A Python component should only appear where it brings a real benefit (an ML library), and that decision is deferred to Stage 2.
- **The Angular "IntelliInvoice" tool** (shown on your portfolio) is Angular, and ClaimWise uses React. Only the *idea* of highlighting extracted fields in a document carries over, and could inspire how citations are highlighted in Stage 2.

## What not to copy

- **Anything built for an employer** (IntelliInvoice, SolutionReach or TVS work). Do not copy code, data, documents or screenshots that belong to employers or clients. Build from your own synthetic data.
- **Unrelated coupling.** Keep ClaimWise in its own repository; do not make it depend on `ba-automation-suite`.

## Small portfolio observations (optional, from what I saw)

- The suite README says "5 specialized modules" but lists six folders.
- Folder names differ between the file list (`01. Status-cleanser`) and the README tree (`01-Status-Cleanser`).
- Recruiters read READMEs closely, so fixing these is cheap polish. I have not changed anything.
