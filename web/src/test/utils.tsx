import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi } from 'vitest';
import type {
  AiSummary, ClaimDetail, ClaimSummary, Dashboard, PagedResult, PolicyDocument,
} from '../api/types';
import { ReviewerProvider } from '../lib/reviewer';

type Handler = (url: URL, init: RequestInit) => unknown | Response | Promise<unknown | Response>;

export const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

/**
 * Replaces fetch with a route table keyed "METHOD /path". Unmatched requests fail the test loudly.
 * Returns the mock so tests can assert on the exact requests that were made.
 */
export function mockApi(routes: Record<string, Handler | unknown>) {
  const fn = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const url = new URL(String(input), 'http://localhost');
    const key = `${(init.method ?? 'GET').toUpperCase()} ${url.pathname}`;
    const route = routes[key];
    if (route === undefined) throw new Error(`Unmocked request: ${key}${url.search}`);
    const result = typeof route === 'function' ? await (route as Handler)(url, init) : route;
    return result instanceof Response ? result : jsonResponse(result);
  });
  vi.stubGlobal('fetch', fn);
  return fn;
}

export const requestsTo = (fn: ReturnType<typeof mockApi>, method: string, path: string) =>
  fn.mock.calls
    .filter(([input, init]) => new URL(String(input), 'http://localhost').pathname === path && ((init as RequestInit | undefined)?.method ?? 'GET') === method)
    .map(([input, init]) => ({ url: new URL(String(input), 'http://localhost'), init: (init ?? {}) as RequestInit }));

export const bodyOf = (call: { init: RequestInit }) => JSON.parse(String(call.init.body));

export function renderAt(ui: ReactElement, path = '/', routePath?: string) {
  return render(
    <ReviewerProvider>
      <MemoryRouter initialEntries={[path]}>
        {routePath ? <Routes><Route path={routePath} element={ui} /><Route path="*" element={<p>elsewhere</p>} /></Routes> : ui}
      </MemoryRouter>
    </ReviewerProvider>,
  );
}

// ---------- fixtures ----------

export const claimRow = (over: Partial<ClaimSummary> = {}): ClaimSummary => ({
  id: 1, claimNumber: 'CW-000001', patientName: 'Anita Rao', providerName: 'Lakeview Demo Clinic', claimType: 'Outpatient',
  amountClaimed: 1850, serviceDate: '2026-10-07', status: 'Submitted', submittedAt: '2026-10-09T08:00:00Z', ...over,
});

export const paged = <T,>(items: T[], over: Partial<PagedResult<T>> = {}): PagedResult<T> => ({
  items, page: 1, pageSize: 10, totalCount: items.length, totalPages: 1, ...over,
});

export const dashboard: Dashboard = {
  totalClaims: 8,
  byStatus: { Submitted: 4, InReview: 1, NeedsInformation: 1, Approved: 1, Rejected: 1 },
  totalAmountClaimed: 183171.25,
  openClaimsWithIncompleteSubmission: 4,
};

export const claimDetail = (over: Partial<ClaimDetail> = {}): ClaimDetail => ({
  id: 1, claimNumber: 'CW-000001', patientName: 'Anita Rao', memberId: 'MBR-100001', policyNumber: 'POL-GOLD-01',
  providerName: 'Lakeview Demo Clinic', claimType: 'Outpatient', diagnosisCode: 'J06.9', procedureCode: 'PRC-101',
  amountClaimed: 1850, serviceDate: '2026-10-07', status: 'InReview', submittedAt: '2026-10-09T08:00:00Z',
  updatedAt: '2026-10-09T08:00:00Z', version: 3, reviewerName: null, reviewerNote: null,
  documents: [{ id: 1, documentType: 'ClaimForm', fileName: 'form.pdf', receivedAt: '2026-10-09T08:00:00Z', sizeBytes: 1200, sha256: 'a', hasFile: true }],
  validation: { isComplete: false, requiredDocuments: ['ClaimForm', 'Invoice', 'MedicalReport'], missingDocuments: ['Invoice', 'MedicalReport'], missingFields: [] },
  allowedNextStatuses: ['NeedsInformation', 'Approved', 'Rejected'],
  ...over,
});

export const policyDoc = (over: Partial<PolicyDocument> = {}): PolicyDocument => ({
  id: 1, title: 'Gold Plan Policy', policyFamily: 'gold-plan', version: '2024.1', effectiveDate: null, fileName: 'gold.md',
  sizeBytes: 4096, pageCount: 1, chunkCount: 28, isActive: true, uploadedAt: '2026-10-09T08:00:00Z', uploadedBy: 'Admin', sha256: 'abc', ...over,
});

const passage = {
  label: 'P1', rank: 1, chunkId: 10, documentId: 1, documentTitle: 'Gold Plan Policy', documentVersion: '2024.1', clauseRef: '2.1',
  heading: '2 Outpatient benefits', sectionPath: 'Gold Plan > 2 Outpatient benefits', pageNumber: 1,
  text: '2.1 Annual outpatient limit. The plan pays outpatient expenses up to 5,000 per member per policy year.',
  rerankScore: 0.82, keywordScore: 7.1, vectorScore: 0.55,
};

export const answeredSummary = (over: Partial<AiSummary> = {}): AiSummary => ({
  id: 7, claimId: 1, question: 'annual outpatient limit', status: 'Answered', generatedAt: '2026-10-10T09:00:00Z', requestedBy: 'Rita',
  generator: 'extractive', model: 'extractive-v1', embeddingModel: 'local-hashing-v1-512', usedLanguageModel: false, claimVersionAtGeneration: 3,
  statements: [{ ordinal: 1, origin: 'Model', text: 'The plan pays outpatient expenses up to 5,000 per member per policy year.',
    citations: [{ passage: 'P1', quote: 'The plan pays outpatient expenses up to 5,000' }] }],
  passages: [passage],
  evidence: { verdict: 'Sufficient', reasons: [], topScore: 0.82, coverage: 0.9, supportingPassages: 1, missingTerms: [] },
  conflicts: [], warnings: [], rejectedStatements: [], abstainReason: null, failureReason: null,
  review: { state: 'PendingReview', reviewedBy: null, reviewedAt: null, note: null },
  notice: 'AI-assisted evidence summary for a human reviewer. It is not a coverage decision and does not change the claim status.',
  ...over,
});
