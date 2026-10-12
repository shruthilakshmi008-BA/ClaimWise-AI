import type {
  AiStatus, AiSummary, AuditEntry, ClaimDetail, ClaimDocument, ClaimListParams, ClaimStatus, ClaimSummary,
  CreateClaimInput, Dashboard, DocumentType, PagedResult, PolicyChunk, PolicyDocument, PolicySearchResponse,
} from './types';

/** An error response from the API, normalised from RFC 7807 problem details. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly title: string,
    message: string,
    /** Field-level validation messages keyed by camelCase field name. */
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

const BASE = (import.meta.env.VITE_API_BASE as string | undefined) ?? '';

const lowerFirst = (s: string) => (s ? s.charAt(0).toLowerCase() + s.slice(1) : s);

async function toApiError(response: Response): Promise<ApiError> {
  let title = response.statusText || 'Request failed';
  let detail = '';
  let fieldErrors: Record<string, string[]> = {};
  try {
    const body = await response.json();
    title = body.title ?? title;
    detail = body.detail ?? '';
    if (body.errors && typeof body.errors === 'object') {
      fieldErrors = Object.fromEntries(
        Object.entries(body.errors as Record<string, string[]>).map(([k, v]) => [lowerFirst(k.replace(/^\$\./, '')), v]),
      );
      detail ||= Object.values(fieldErrors).flat().join(' ');
    }
  } catch {
    /* not JSON: keep the status text */
  }
  if (!detail) {
    detail =
      response.status === 429 ? 'Too many requests. Please wait a moment and try again.'
      : response.status >= 500 ? 'The server could not complete the request.'
      : title;
  }
  return new ApiError(response.status, title, detail, fieldErrors);
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${BASE}${path}`, {
      ...init,
      headers: { Accept: 'application/json', ...(init?.body && !(init.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}), ...init?.headers },
    });
  } catch {
    throw new ApiError(0, 'Network error', 'The API could not be reached. Is the backend running?');
  }
  if (!response.ok) throw await toApiError(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });

function query(params: Record<string, string | number | undefined | null>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') q.set(k, String(v));
  const s = q.toString();
  return s ? `?${s}` : '';
}

export const api = {
  // claims
  dashboard: () => request<Dashboard>('/api/claims/summary'),
  listClaims: (p: ClaimListParams) => request<PagedResult<ClaimSummary>>(`/api/claims${query({ ...p })}`),
  getClaim: (id: number) => request<ClaimDetail>(`/api/claims/${id}`),
  createClaim: (input: CreateClaimInput) => request<ClaimDetail>('/api/claims', json('POST', input)),
  audit: (id: number) => request<AuditEntry[]>(`/api/claims/${id}/audit`),
  changeStatus: (id: number, body: { newStatus: ClaimStatus; reviewerName: string; note?: string; expectedVersion?: number }) =>
    request<ClaimDetail>(`/api/claims/${id}/status`, json('POST', body)),
  uploadClaimDocument: (id: number, documentType: DocumentType, file: File) => {
    const form = new FormData();
    form.append('DocumentType', documentType);
    form.append('File', file);
    return request<ClaimDocument>(`/api/claims/${id}/documents/upload`, { method: 'POST', body: form });
  },
  claimDocumentUrl: (claimId: number, documentId: number) => `${BASE}/api/claims/${claimId}/documents/${documentId}/file`,

  // AI summaries
  listSummaries: (id: number) => request<AiSummary[]>(`/api/claims/${id}/summaries`),
  generateSummary: (id: number, body: { question?: string; requestedBy: string; documentIds?: number[] }) =>
    request<AiSummary>(`/api/claims/${id}/summaries`, json('POST', body)),
  reviewSummary: (id: number, summaryId: number, body: { decision: 'Accepted' | 'Rejected'; reviewerName: string; note?: string }) =>
    request<AiSummary>(`/api/claims/${id}/summaries/${summaryId}/review`, json('POST', body)),

  // policies
  listPolicies: () => request<PolicyDocument[]>('/api/policies'),
  policyChunks: (id: number) => request<PolicyChunk[]>(`/api/policies/${id}/chunks`),
  uploadPolicy: (form: FormData) => request<PolicyDocument>('/api/policies', { method: 'POST', body: form }),
  setPolicyActive: (id: number, isActive: boolean) => request<PolicyDocument>(`/api/policies/${id}/active`, json('PATCH', { isActive })),
  reindexPolicies: () => request<{ reindexedChunks: number }>('/api/policies/reindex', json('POST')),
  searchPolicies: (body: { query: string; topK?: number; mode?: string; documentIds?: number[] }) =>
    request<PolicySearchResponse>('/api/policies/search', json('POST', body)),
  aiStatus: () => request<AiStatus>('/api/ai/status'),
};
