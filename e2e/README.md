# End-to-end browser check

Drives the real UI in headless Chromium through the main workflow and asserts on what the page shows:
dashboard → create claim → upload documents → move to *In review* → retrieve policy evidence and a cited summary
(conflict banner, verbatim citation highlighted) → approval blocked until the AI summary is reviewed → human review →
human decision → audit order → abstention on an off-topic question → list filtering → policy search → mobile width.

It needs a running stack **with fresh seed data** (it expects the 8 seeded claims and the 4 sample policies, and it
creates a claim, so run it against a new database each time):

```bash
# terminal 1 – API (Development seeds claims and policies; use a throw-away database)
cd src/ClaimWise.Api
ConnectionStrings__Default="Data Source=/tmp/claimwise-e2e.db" ASPNETCORE_ENVIRONMENT=Development dotnet run

# terminal 2 – UI
cd web && npm install && npm run dev

# terminal 3 – the check
cd e2e && npm install
CHROMIUM_PATH=/path/to/chrome node run.mjs        # or set PLAYWRIGHT_BROWSERS_PATH
```

Screenshots are written to `./screenshots` (git-ignored). Exit code is non-zero if any check fails.
