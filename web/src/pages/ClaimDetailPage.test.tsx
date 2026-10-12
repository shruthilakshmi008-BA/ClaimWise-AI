import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { ClaimDetailPage } from './ClaimDetailPage';
import { answeredSummary, bodyOf, claimDetail, jsonResponse, mockApi, policyDoc, renderAt, requestsTo } from '../test/utils';
import type { AiSummary, ClaimDetail } from '../api/types';

function setup(opts: { claim?: ClaimDetail; summaries?: AiSummary[]; extra?: Record<string, unknown> } = {}) {
  const fetchMock = mockApi({
    'GET /api/claims/1': opts.claim ?? claimDetail(),
    'GET /api/claims/1/audit': [{ id: 1, action: 'ClaimCreated', details: 'Outpatient claim submitted.', actor: 'intake', timestamp: '2026-10-09T08:00:00Z' }],
    'GET /api/claims/1/summaries': opts.summaries ?? [],
    'GET /api/policies': [policyDoc()],
    ...opts.extra,
  });
  renderAt(<ClaimDetailPage />, '/claims/1', '/claims/:id');
  return { fetchMock, user: userEvent.setup() };
}

describe('ClaimDetailPage', () => {
  it('shows the claim, what is missing, and the audit history', async () => {
    setup();

    expect(await screen.findByRole('heading', { name: /CW-000001/ })).toBeInTheDocument();
    expect(screen.getByText(/Incomplete submission/)).toBeInTheDocument();
    expect(screen.getByText(/Missing documents: Invoice, Medical report/)).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Required documents' })).getByRole('link', { name: 'form.pdf' })).toHaveAttribute('href', '/api/claims/1/documents/1/file');
    expect(await screen.findByText('Outpatient claim submitted.')).toBeInTheDocument();
  });

  it('requires a reviewer name and a note before a decision, without calling the API', async () => {
    const { fetchMock, user } = setup();
    await screen.findByRole('heading', { name: /CW-000001/ });

    await user.click(screen.getByRole('button', { name: 'Move to Rejected' }));
    expect(await screen.findByText(/Enter your name/)).toBeInTheDocument();

    await user.type(screen.getByLabelText('Your name (reviewer)'), 'Rita Reviewer');
    await user.click(screen.getByRole('button', { name: 'Move to Rejected' }));
    expect(await screen.findByText(/A note is required when moving a claim to Rejected/)).toBeInTheDocument();
    expect(requestsTo(fetchMock, 'POST', '/api/claims/1/status')).toHaveLength(0);
  });

  it('records a decision with the reviewer, note and the version the reviewer saw', async () => {
    const { fetchMock, user } = setup({
      extra: { 'POST /api/claims/1/status': () => jsonResponse(claimDetail({ status: 'NeedsInformation', version: 4 })) },
    });
    await screen.findByRole('heading', { name: /CW-000001/ });
    await user.type(screen.getByLabelText('Your name (reviewer)'), 'Rita Reviewer');
    await user.type(screen.getByLabelText('Note'), 'Please send the invoice.');

    await user.click(screen.getByRole('button', { name: 'Move to Needs information' }));

    await waitFor(() => expect(requestsTo(fetchMock, 'POST', '/api/claims/1/status')).toHaveLength(1));
    expect(bodyOf(requestsTo(fetchMock, 'POST', '/api/claims/1/status')[0]!)).toEqual({
      newStatus: 'NeedsInformation', reviewerName: 'Rita Reviewer', note: 'Please send the invoice.', expectedVersion: 3,
    });
  });

  it('shows the server explanation when a decision is refused', async () => {
    const { user } = setup({
      extra: { 'POST /api/claims/1/status': () => jsonResponse({ title: 'Business rule violated', status: 409, detail: 'Cannot approve an incomplete claim. Missing: document Invoice.' }, 409) },
    });
    await screen.findByRole('heading', { name: /CW-000001/ });
    await user.type(screen.getByLabelText('Your name (reviewer)'), 'Rita Reviewer');
    await user.type(screen.getByLabelText('Note'), 'Trying anyway');

    await user.click(screen.getByRole('button', { name: 'Move to Approved' }));

    expect(await screen.findByText('Cannot approve an incomplete claim. Missing: document Invoice.')).toBeInTheDocument();
  });

  it('keeps Approve and Reject disabled while an AI summary awaits human review', async () => {
    setup({ summaries: [answeredSummary()] });
    await screen.findByRole('heading', { name: /CW-000001/ });

    expect(await screen.findByText(/1 AI summary is awaiting your review/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Move to Approved' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Move to Rejected' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Move to Needs information' })).toBeEnabled();
  });

  it('does not offer any actions on a final claim', async () => {
    setup({ claim: claimDetail({ status: 'Approved', allowedNextStatuses: [] }) });
    expect(await screen.findByText(/Final claims cannot be changed/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Move to/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Generate AI summary' })).not.toBeInTheDocument();
  });

  it('uploads a document of the chosen type', async () => {
    const { fetchMock, user } = setup({ extra: { 'POST /api/claims/1/documents/upload': () => jsonResponse({ id: 9 }, 201) } });
    await screen.findByRole('heading', { name: /CW-000001/ });

    const form = screen.getByRole('form', { name: 'Upload document' });
    await user.upload(within(form).getByLabelText(/File/), new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' }));
    await user.click(within(form).getByRole('button', { name: 'Upload' }));

    await waitFor(() => expect(requestsTo(fetchMock, 'POST', '/api/claims/1/documents/upload')).toHaveLength(1));
    const sent = requestsTo(fetchMock, 'POST', '/api/claims/1/documents/upload')[0]!.init.body as FormData;
    expect(sent.get('DocumentType')).toBe('Invoice');           // first missing required document
    expect((sent.get('File') as File).name).toBe('invoice.pdf');
  });
});
