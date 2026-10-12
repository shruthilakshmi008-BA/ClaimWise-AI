// End-to-end browser check of the main reviewer workflow against a RUNNING stack (API + UI) with FRESH seed data.
// See e2e/README.md. Configuration (all optional):
//   BASE_URL      UI address                      (default http://127.0.0.1:5173)
//   CHROMIUM_PATH Chromium/Chrome executable      (default: Playwright's lookup via PLAYWRIGHT_BROWSERS_PATH)
//   SHOTS_DIR     where to write screenshots      (default ./screenshots)
import { chromium } from 'playwright-core';
import { mkdirSync, readdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const BASE = process.env.BASE_URL ?? 'http://127.0.0.1:5173';
const SHOTS = process.env.SHOTS_DIR ?? './screenshots';
mkdirSync(SHOTS, { recursive: true });
const FILES = join(tmpdir(), 'claimwise-e2e-files');
mkdirSync(FILES, { recursive: true });
for (const f of ['claim-form.pdf', 'invoice.pdf', 'medical-report.pdf']) writeFileSync(join(FILES, f), '%PDF-1.4\n1 0 obj\n<<>>\nendobj\n');

function findChromium() {
  if (process.env.CHROMIUM_PATH) return process.env.CHROMIUM_PATH;
  const root = process.env.PLAYWRIGHT_BROWSERS_PATH;
  if (root) {
    const dir = readdirSync(root).find((d) => /^chromium-\d+$/.test(d));
    if (dir) return join(root, dir, 'chrome-linux', 'chrome');
  }
  return undefined; // let Playwright try its default
}
const results = [];
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? ' — ' + extra : ''}`); };

const browser = await chromium.launch({ executablePath: findChromium(), args: ['--no-sandbox'] });
const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
const consoleErrors = [];
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', (e) => consoleErrors.push(String(e)));

try {
  // 1. Dashboard shows real seeded numbers
  await page.goto(BASE + '/');
  await page.getByText('Total claims').waitFor();
  check('dashboard total claims = 8', (await page.locator('.tile').first().innerText()).includes('8'));
  check('dashboard AI status is honest (extractive)', await page.getByText('Extractive quoting — no language model').isVisible());
  await page.screenshot({ path: `${SHOTS}/01-dashboard.png`, fullPage: true });

  // 2. Create a claim through the form
  await page.getByRole('link', { name: 'New claim', exact: true }).first().click();
  await page.getByRole('button', { name: 'Submit claim' }).click();
  check('client validation blocks empty submit', await page.getByText('Choose a claim type.').isVisible());
  await page.getByLabel('Patient name').fill('Ethan Browser-Test');
  await page.getByLabel('Claim type').selectOption('Outpatient');
  await page.getByLabel('Member ID').fill('MBR-880001');
  await page.getByLabel('Policy number').fill('POL-GOLD-77');
  await page.getByLabel('Provider').fill('E2E Demo Clinic');
  const d = new Date(Date.now() - 3 * 86400000).toISOString().slice(0, 10);
  await page.getByLabel('Service date').fill(d);
  await page.getByLabel('Diagnosis code').fill('J06.9');
  await page.getByLabel('Procedure code').fill('PRC-101');
  await page.getByLabel('Amount claimed').fill('1850.00');
  await page.getByRole('button', { name: 'Submit claim' }).click();
  await page.getByRole('heading', { name: /CW-0000\d+/ }).waitFor();
  const claimNo = (await page.getByRole('heading', { name: /CW-0000\d+/ }).innerText()).match(/CW-\d+/)[0];
  check('claim created and detail page opened', true, claimNo);
  check('new claim is flagged incomplete', await page.getByText('Incomplete submission.').isVisible());

  // 3. Upload the three required documents
  for (const [type, file] of [['Claim form', 'claim-form.pdf'], ['Invoice', 'invoice.pdf'], ['Medical report', 'medical-report.pdf']]) {
    const form = page.getByRole('form', { name: 'Upload document' });
    await form.getByLabel('Document type').selectOption({ label: type });
    await form.getByLabel(/^File/).setInputFiles(`${FILES}/${file}`);
    await form.getByRole('button', { name: 'Upload' }).click();
    await page.getByRole('list', { name: 'Required documents' }).getByRole('link', { name: file }).waitFor();
  }
  check('all documents uploaded, submission complete', await page.getByText('Submission is complete').isVisible());
  await page.screenshot({ path: `${SHOTS}/02-claim-complete.png`, fullPage: true });

  // 4. Move to In review as a named reviewer
  await page.getByLabel('Your name (reviewer)').fill('Rita Reviewer');
  await page.getByRole('button', { name: 'Move to In review' }).click();
  await page.locator('h1 .badge', { hasText: 'In review' }).waitFor();
  check('claim moved to In review by a named reviewer', true);

  // 5. Retrieve policy evidence + cited summary
  await page.getByLabel(/^Question/).fill('annual outpatient limit and co-payment');
  await page.getByRole('button', { name: 'Find policy evidence' }).click();
  const card = page.getByRole('article', { name: /AI summary/ }).first();
  await card.waitFor();
  check('summary shows findings', await card.getByText('Findings').isVisible());
  check('summary is marked as awaiting human review', await card.getByText('Awaiting human review').first().isVisible());
  check('conflicting policy wording is surfaced (5,000 vs 6,000)', (await card.locator('.notice-conflict').innerText()).includes('6,000'));
  check('mixed-plan warning shown', await card.getByText(/different policies/).isVisible());
  await card.getByRole('button', { name: '[P1]' }).first().click();
  const source = card.locator('blockquote.source').first();
  const marked = await source.locator('mark').innerText();
  check('citation reveals the verbatim source with highlighted quote', marked.length > 10, marked.slice(0, 60));
  await page.screenshot({ path: `${SHOTS}/03-ai-summary.png`, fullPage: true });

  // 6. Decision is blocked until the summary is reviewed
  check('Approve disabled while AI summary awaits review', await page.getByRole('button', { name: 'Move to Approved' }).isDisabled());
  await card.getByLabel('Note (required to reject)').fill('Checked clauses 2.1 and 2.2 against the policy; amendment noted.');
  await card.getByRole('button', { name: 'Accept summary' }).click();
  await card.getByText(/Accepted by/).last().waitFor();
  check('summary accepted by named reviewer', true);

  // 7. Human decision
  await page.getByRole('button', { name: 'Move to Approved' }).waitFor();
  check('Approve enabled after human review', await page.getByRole('button', { name: 'Move to Approved' }).isEnabled());
  await page.getByLabel('Note', { exact: true }).fill('Documents complete; limit confirmed per amended policy clause 2.1.');
  await page.getByRole('button', { name: 'Move to Approved' }).click();
  await page.locator('h1 .badge', { hasText: 'Approved' }).waitFor();
  check('claim approved by the human reviewer', true);

  const audit = await page.locator('.timeline li').allInnerTexts();
  const auditText = audit.join('\n');
  check('audit shows AI generation, human review, then decision', /Ai summary generated[\s\S]*Ai summary reviewed[\s\S]*Status changed/.test(auditText));
  check('audit records AI-reviewed note on the decision', auditText.includes('AI summaries reviewed by a person'));
  await page.screenshot({ path: `${SHOTS}/04-approved-audit.png`, fullPage: true });
  check('final claim offers no further actions', (await page.getByRole('button', { name: /Move to/ }).count()) === 0);

  // 8. Abstention on a second (seeded) claim
  await page.goto(BASE + '/claims?status=Submitted');
  await page.getByRole('link', { name: 'CW-000003' }).click();
  await page.getByRole('heading', { name: 'CW-000003' }).waitFor();
  await page.getByLabel(/^Question/).fill('quantum computing satellite launch insurance');
  await page.getByRole('button', { name: 'Find policy evidence' }).click();
  const abst = page.getByRole('article', { name: /AI summary/ }).first();
  await abst.getByText('Insufficient evidence — no findings shown.').waitFor();
  check('off-topic question abstains with no findings', (await abst.locator('.statements').count()) === 0);
  await page.screenshot({ path: `${SHOTS}/05-abstained.png`, fullPage: true });

  // 9. List filtering / sorting
  await page.goto(BASE + '/claims');
  await page.getByLabel('Status').selectOption('NeedsInformation');
  await page.waitForFunction(() => document.querySelectorAll('tbody tr').length === 1);
  check('status filter narrows the list', true);
  await page.getByLabel('Status').selectOption('');
  await page.getByLabel('Search').fill('Lakeview');
  await page.waitForFunction(() => [...document.querySelectorAll('tbody tr')].every((r) => r.textContent.includes('Lakeview')) && document.querySelectorAll('tbody tr').length >= 1);
  check('search narrows the list', true);

  // 10. Policies page + search
  await page.goto(BASE + '/policies');
  await page.getByText('Gold Plan Policy (Version 2024.1)').first().waitFor();
  await page.getByLabel('Question or keywords').fill('dental waiting period');
  await page.getByRole('button', { name: 'Search' }).click();
  await page.getByText(/clause 5\.2/).first().waitFor();
  const scoreLine = await page.locator('ol.passages li').first().innerText(); check('policy search finds clause 5.2 with stage scores', /clause 5\.2/.test(scoreLine) && /keyword [\d.]+ \(rank 1\)/.test(scoreLine) && /vector [\d.]+ \(rank/.test(scoreLine), scoreLine.replace(/\n/g,' ').slice(0,120));
  await page.screenshot({ path: `${SHOTS}/06-policies.png`, fullPage: true });

  // 11. Mobile layout
  const mobile = await (await browser.newContext({ viewport: { width: 390, height: 800 } })).newPage();
  await mobile.goto(BASE + '/claims');
  await mobile.getByRole('link', { name: 'CW-000001' }).waitFor();
  const overflow = await mobile.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
  check('no horizontal scroll at 390px width', !overflow);
  await mobile.screenshot({ path: `${SHOTS}/07-mobile-claims.png`, fullPage: true });

  check('no console errors in the browser', consoleErrors.length === 0, consoleErrors.slice(0, 2).join(' | '));
} catch (e) {
  console.log('E2E ABORTED:', e.message.split('\n')[0]);
  await page.screenshot({ path: `${SHOTS}/failure.png`, fullPage: true }).catch(() => {});
  results.push({ name: 'run completed', ok: false });
} finally {
  await browser.close();
}
const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exit(failed.length ? 1 : 0);
