# ClaimWise AI

A healthcare claims review application for claims reviewers, built as a portfolio project.
It helps a reviewer log claims, see what is missing from a submission, move claims through a controlled review workflow, and keep an audit trail of every change.

> **Status: Stage 1 of 3 (basic claims workflow, backend only).**
> Document ingestion, hybrid retrieval, reranking, grounded AI summaries and citations are planned for Stage 2 and are **not implemented yet**. The React frontend is planned as Stage 1b.
> See [docs/progress.md](docs/progress.md) for the exact checklist.

**All data is synthetic.** Names, member IDs, providers and claims are fictional. Do not enter real patient or insurance data. This is a demo, not medical advice, a coverage decision tool, or a compliance-certified system (no HIPAA or other regulatory claim is made).

## The problem

Claims reviewers collect claim details, check supporting documents, and track progress by hand. That is slow, inconsistent and hard to audit.

## What Stage 1 does

- Claim intake with server-side validation
- Claim list with **search, filters, sorting and pagination**
- Dashboard summary (counts per status, total claimed, incomplete open claims)
- **Required-document and missing-field checks** that depend on claim type
- A **status workflow** (`Submitted → InReview → Approved / Rejected`, or `NeedsInformation`) with rules about which moves are allowed
- **Human decisions only:** approving or rejecting needs a named reviewer and a written note, and a claim cannot be approved while its submission is incomplete
- **Audit history** of every creation, document and status change
- Consistent JSON error responses, structured logging, interactive API docs
- Automated unit and integration tests

## Technology

| Layer | Choice | Why |
|---|---|---|
| Language / framework | C# on .NET 10 (LTS), ASP.NET Core Web API | Your stated preference; .NET 10 is supported until November 2028 |
| Data access | Entity Framework Core + SQLite | No database server to install; PostgreSQL is the planned deployment option |
| API docs | Built-in OpenAPI + Scalar UI | Interactive page at `/scalar/v1` in development |
| Tests | xUnit + ASP.NET Core `WebApplicationFactory` | Unit tests for rules, integration tests through real HTTP |

All of these are free and open source. See [docs/decisions.md](docs/decisions.md) for alternatives and trade-offs.

## Folder structure

```
claimwise-ai/
├── ClaimWise.slnx                     solution file (groups the two projects)
├── .gitignore
├── README.md
├── docs/                              requirements, architecture, decisions, progress
├── src/ClaimWise.Api/
│   ├── Program.cs                     startup: wires everything together
│   ├── appsettings.json               configuration (connection string, CORS, seed flag)
│   ├── Properties/launchSettings.json fixes the dev URL to http://localhost:5080
│   ├── Domain/                        entities, enums, business rules (no framework code)
│   ├── Dtos/                          request/response shapes + input validation
│   ├── Data/                          EF Core DbContext + synthetic seed data
│   ├── Services/                      ClaimService: the business logic
│   ├── Controllers/                   ClaimsController: the HTTP endpoints
│   └── Infrastructure/                error handling
└── tests/ClaimWise.Tests/
    ├── DomainRulesTests.cs            unit tests (rules + workflow)
    └── ClaimsApiTests.cs              integration tests (full HTTP requests)
```

## Setup on Windows (fresh machine)

Use **Windows PowerShell** or the terminal inside VS Code (`` Ctrl+` ``).

### 1. Install the tools

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Git.Git
winget install Microsoft.VisualStudioCode
```

If `winget` is not available, download the **.NET 10 SDK** from <https://dotnet.microsoft.com/download/dotnet/10.0>, Git from <https://git-scm.com>, and VS Code from <https://code.visualstudio.com>.

**Close and reopen PowerShell**, then verify:

```powershell
dotnet --version
git --version
```

Expected: `dotnet --version` prints something starting with `10.0.` and `git --version` prints a version number. If `dotnet` is "not recognized", restart your computer and try again.

### 2. VS Code extensions

In VS Code open Extensions (`Ctrl+Shift+X`) and install **C# Dev Kit** (it installs the **C#** extension with it). Optional: **REST Client** and **GitLens**.

### 3. Open the project

Unzip the project, for example to `C:\dev\claimwise-ai`, then:

```powershell
cd C:\dev\claimwise-ai
code .
```

### 4. Restore, build and test (run from `C:\dev\claimwise-ai`)

```powershell
dotnet restore
dotnet build
dotnet test
```

- `restore` downloads the libraries listed in the `.csproj` files (needs internet the first time).
- `build` compiles the code. Expected: `Build succeeded` with `0 Error(s)`.
- `test` runs all tests. Expected: a line like `Passed!  - Failed: 0, Passed: 44, Skipped: 0` (29 unit-test cases + 15 integration tests, by my count).

> **Important:** this project was written without being able to download NuGet packages in the authoring environment, so the full build and the test run have **not** been executed by the author. The domain rules, DTO validation, controller and error handler were compiled and exercised separately; the database code and the xUnit tests were not. If `dotnet build` or `dotnet test` prints any error, copy the **complete** terminal output and send it to me; do not edit the code yourself first.

### 5. Run the API (from `C:\dev\claimwise-ai\src\ClaimWise.Api`)

```powershell
cd src\ClaimWise.Api
dotnet run
```

Expected output includes `Now listening on: http://localhost:5080`. On first start the app creates `claimwise.db` and loads 8 synthetic claims.

### 6. Verify it works

Open these in a browser:

- <http://localhost:5080/health> → `{"status":"healthy"}`
- <http://localhost:5080/scalar/v1> → interactive API documentation where you can try each endpoint

Or in a **second** PowerShell window:

```powershell
Invoke-RestMethod http://localhost:5080/api/claims/summary
```

With the untouched seed data, the summary should show `totalClaims` 8, `totalAmountClaimed` 183171.25 and `openClaimsWithIncompleteSubmission` 4 (worked out by hand from the seed file; confirm it when you run it).

### 7. Stop, restart and reset

- Stop: press `Ctrl+C` in the window running the API.
- Restart: run `dotnet run` again. Data persists in `claimwise.db`.
- Reset to fresh demo data: stop the API, then `Remove-Item .\claimwise.db` (inside `src\ClaimWise.Api`) and run again.

## API examples (PowerShell)

```powershell
# List claims: filter, sort, paginate
Invoke-RestMethod "http://localhost:5080/api/claims?status=Submitted&sortBy=amount&sortDir=desc&page=1&pageSize=5"

# Search
Invoke-RestMethod "http://localhost:5080/api/claims?search=Lakeview"

# One claim (with documents, validation result, allowed next statuses)
Invoke-RestMethod http://localhost:5080/api/claims/1

# Create a claim
$body = @{
  patientName="Test Patient"; memberId="MBR-900001"; policyNumber="POL-TEST-01"
  providerName="Demo Clinic"; claimType="Outpatient"; diagnosisCode="J06.9"
  procedureCode="PRC-101"; amountClaimed=1200; serviceDate="2026-10-01"
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/claims -ContentType "application/json" -Body $body

# Record a received document
$doc = @{ documentType="MedicalReport"; fileName="report-001.pdf" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/claims/1/documents -ContentType "application/json" -Body $doc

# Reviewer moves the claim forward
$s = @{ newStatus="InReview"; reviewerName="Test Reviewer" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/claims/1/status -ContentType "application/json" -Body $s

# Audit history
Invoke-RestMethod http://localhost:5080/api/claims/1/audit
```

## Common problems

| Symptom | Likely cause and fix |
|---|---|
| `dotnet` is not recognized | Reopen PowerShell; if it persists restart Windows; confirm the SDK installed |
| `Address already in use` on port 5080 | Another copy is running. Close it, or change `applicationUrl` in `Properties/launchSettings.json` |
| `dotnet restore` fails with network errors | Check internet/proxy access to `api.nuget.org` |
| `dotnet test` fails | Send me the full output. Do not delete tests to make them pass |
| Frontend cannot reach the API later | The frontend origin must be listed under `Cors:AllowedOrigins` in `appsettings.json` |

## Security and privacy notes

- Synthetic data only. `.gitignore` excludes `*.db` and `.env` files so databases and secrets are not committed.
- Inputs are validated on the server; database access goes through EF Core, which parameterizes queries.
- **There is no authentication yet.** The "reviewer name" on a status change is self-declared, so the audit trail records who *said* they acted, not a verified identity. Authentication and roles come before any deployment.

## Limitations

- Documents are recorded as metadata only (type and file name); no file upload yet.
- `EnsureCreated` creates the database; proper migrations come before deployment.
- No authentication or roles; no rate limiting yet.
- No AI features yet.

## Roadmap

1. **Stage 1b:** React + TypeScript frontend (dashboard, claim list, claim detail, review actions)
2. **Stage 2:** policy document ingestion, hybrid keyword + vector retrieval, reranking, evidence-grounded summaries with citations, abstention when evidence is weak, retrieval evaluation set
3. **Stage 3:** authentication/roles, migrations, PostgreSQL, CI, deployment

Repository and live-demo links: _not published yet._
