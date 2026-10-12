import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ClaimsPage } from './ClaimsPage';
import { claimRow, mockApi, paged, renderAt, requestsTo } from '../test/utils';

const lastQuery = (fn: ReturnType<typeof mockApi>) => Object.fromEntries(requestsTo(fn, 'GET', '/api/claims').at(-1)!.url.searchParams);

describe('ClaimsPage', () => {
  it('lists claims with status badges and links to the detail page', async () => {
    mockApi({ 'GET /api/claims': paged([claimRow(), claimRow({ id: 2, claimNumber: 'CW-000002', status: 'NeedsInformation', patientName: 'Karan Shetty' })]) });
    renderAt(<ClaimsPage />, '/claims');

    expect(await screen.findByRole('link', { name: 'CW-000001' })).toHaveAttribute('href', '/claims/1');
    expect(screen.getByText('Karan Shetty')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).getByText('Needs information')).toBeInTheDocument();
  });

  it('sends the status, type and sort selections to the API and resets to page 1', async () => {
    const fetchMock = mockApi({ 'GET /api/claims': paged([claimRow()], { page: 2, totalCount: 25, totalPages: 3 }) });
    const user = userEvent.setup();
    renderAt(<ClaimsPage />, '/claims?page=2');
    await screen.findByText('CW-000001');
    expect(lastQuery(fetchMock).page).toBe('2');

    await user.selectOptions(screen.getByLabelText('Status'), 'InReview');
    await user.selectOptions(screen.getByLabelText('Claim type'), 'Dental');
    await user.selectOptions(screen.getByLabelText('Sort by'), 'amount');
    await user.selectOptions(screen.getByLabelText('Direction'), 'asc');

    await waitFor(() => expect(lastQuery(fetchMock)).toMatchObject({ status: 'InReview', claimType: 'Dental', sortBy: 'amount', sortDir: 'asc' }));
    expect(lastQuery(fetchMock).page).toBe('1');
  });

  it('debounces the search box before calling the API', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const fetchMock = mockApi({ 'GET /api/claims': paged([claimRow()]) });
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderAt(<ClaimsPage />, '/claims');
      await screen.findByText('CW-000001');
      const before = requestsTo(fetchMock, 'GET', '/api/claims').length;

      await user.type(screen.getByLabelText('Search'), 'lakeview');
      expect(requestsTo(fetchMock, 'GET', '/api/claims').length).toBe(before); // nothing yet: still typing

      await act(async () => { await vi.advanceTimersByTimeAsync(350); });
      await waitFor(() => expect(lastQuery(fetchMock).search).toBe('lakeview'));
      expect(requestsTo(fetchMock, 'GET', '/api/claims').length).toBe(before + 1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('pages forward and back using the server totals', async () => {
    const fetchMock = mockApi({
      'GET /api/claims': (url: URL) => paged([claimRow()], { page: Number(url.searchParams.get('page') ?? 1), totalCount: 25, totalPages: 3 }),
    });
    const user = userEvent.setup();
    renderAt(<ClaimsPage />, '/claims');
    await screen.findByText(/Page 1 of 3 · 25 claims/);
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: /next/i }));

    await screen.findByText(/Page 2 of 3/);
    expect(lastQuery(fetchMock).page).toBe('2');
  });

  it('shows an empty state and a retryable error', async () => {
    let fail = true;
    mockApi({
      'GET /api/claims': () => (fail ? new Response(JSON.stringify({ title: 'x' }), { status: 500 }) : paged([])),
    });
    const user = userEvent.setup();
    renderAt(<ClaimsPage />, '/claims');

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('The server could not complete the request.')).toBeInTheDocument();

    fail = false;
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('No claims match these filters.')).toBeInTheDocument();
  });
});
