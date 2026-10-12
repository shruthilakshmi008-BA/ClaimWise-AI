import { describe, expect, it, vi } from 'vitest';
import { ApiError, api } from './client';
import { jsonResponse, mockApi, requestsTo } from '../test/utils';

describe('api client', () => {
  it('turns ASP.NET validation problems into camelCase field errors', async () => {
    mockApi({
      'POST /api/claims': () => jsonResponse({ title: 'One or more validation errors occurred.', status: 400, errors: { MemberId: ['MemberId must be 5-20 letters.'], 'ServiceDate': ['ServiceDate cannot be in the future.'] } }, 400),
    });

    const error = await api.createClaim({} as never).catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(400);
    expect(error.fieldErrors.memberId).toEqual(['MemberId must be 5-20 letters.']);
    expect(error.message).toContain('ServiceDate cannot be in the future');
  });

  it('surfaces the detail of business-rule conflicts', async () => {
    mockApi({ 'POST /api/claims/1/status': () => jsonResponse({ title: 'Business rule violated', status: 409, detail: 'Cannot approve an incomplete claim.' }, 409) });
    const error = await api.changeStatus(1, { newStatus: 'Approved', reviewerName: 'Rita' }).catch((e) => e);
    expect(error.status).toBe(409);
    expect(error.message).toBe('Cannot approve an incomplete claim.');
  });

  it('explains rate limiting and server errors without leaking internals', async () => {
    mockApi({ 'GET /api/claims/summary': () => new Response('', { status: 429 }), 'GET /api/ai/status': () => new Response('<html>stack trace</html>', { status: 500 }) });
    expect((await api.dashboard().catch((e) => e)).message).toMatch(/too many requests/i);
    expect((await api.aiStatus().catch((e) => e)).message).toBe('The server could not complete the request.');
  });

  it('reports an unreachable API in plain language', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));
    const error = await api.dashboard().catch((e) => e);
    expect(error.status).toBe(0);
    expect(error.message).toMatch(/could not be reached/i);
  });

  it('builds list queries from the filters and skips empty ones', async () => {
    const fetchMock = mockApi({ 'GET /api/claims': { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 } });
    await api.listClaims({ status: 'InReview', claimType: '', search: 'lake view', sortBy: 'amount', sortDir: 'asc', page: 2, pageSize: 10 });
    const url = requestsTo(fetchMock, 'GET', '/api/claims')[0]!.url;
    expect(Object.fromEntries(url.searchParams)).toEqual({ status: 'InReview', search: 'lake view', sortBy: 'amount', sortDir: 'asc', page: '2', pageSize: '10' });
  });

  it('uploads files as multipart without forcing a JSON content type', async () => {
    const fetchMock = mockApi({ 'POST /api/claims/1/documents/upload': { id: 5 } });
    await api.uploadClaimDocument(1, 'Invoice', new File(['%PDF-1.4'], 'inv.pdf', { type: 'application/pdf' }));
    const call = requestsTo(fetchMock, 'POST', '/api/claims/1/documents/upload')[0]!;
    expect(call.init.body).toBeInstanceOf(FormData);
    expect((call.init.body as FormData).get('DocumentType')).toBe('Invoice');
    expect(JSON.stringify(call.init.headers)).not.toMatch(/content-type/i);
  });
});
