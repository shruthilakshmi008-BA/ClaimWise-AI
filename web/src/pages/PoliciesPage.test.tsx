import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { PoliciesPage } from './PoliciesPage';
import { bodyOf, jsonResponse, mockApi, policyDoc, renderAt, requestsTo } from '../test/utils';

const status = {
  embeddingProvider: 'Local', embeddingModel: 'local-hashing-v1-512', summaryGenerator: 'extractive', summaryModel: 'extractive-v1',
  activeDocuments: 1, indexedChunks: 28, staleChunks: 0, summariesUseLanguageModel: false,
};

describe('PoliciesPage', () => {
  it('lists documents with their metadata and can show the extracted passages', async () => {
    mockApi({
      'GET /api/policies': [policyDoc()], 'GET /api/ai/status': status,
      'GET /api/policies/1/chunks': [{ id: 5, ordinal: 0, clauseRef: '2.1', heading: '2 Outpatient benefits', sectionPath: 'Gold > 2 Outpatient benefits', pageNumber: 1, startOffset: 0, endOffset: 50, wordCount: 18, text: 'Annual outpatient limit. 5,000.' }],
    });
    const user = userEvent.setup();
    renderAt(<PoliciesPage />, '/policies');

    expect(await screen.findByText('Gold Plan Policy')).toBeInTheDocument();
    expect(screen.getByText(/28 passages/)).toBeInTheDocument();
    expect(screen.getByText('gold-plan')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'View passages' }));

    expect(await screen.findByText(/§2.1 · Gold > 2 Outpatient benefits · page 1/)).toBeInTheDocument();
  });

  it('uploads a file with its metadata as multipart form data', async () => {
    const fetchMock = mockApi({ 'GET /api/policies': [], 'GET /api/ai/status': status, 'POST /api/policies': () => jsonResponse(policyDoc(), 201) });
    const user = userEvent.setup();
    renderAt(<PoliciesPage />, '/policies');
    const form = await screen.findByRole('form', { name: 'Upload policy' });

    await user.upload(within(form).getByLabelText(/^File/), new File(['# Policy\n\n1.1 Text'], 'policy.md', { type: 'text/markdown' }));
    expect(within(form).getByRole('button', { name: 'Upload and index' })).toBeDisabled(); // needs an uploader name
    await user.type(within(form).getByLabelText('Uploaded by'), 'Policy Admin');
    await user.type(within(form).getByLabelText(/Policy family/), 'gold-plan');
    await user.type(within(form).getByLabelText(/^Version/), '2024.1');
    await user.click(within(form).getByRole('button', { name: 'Upload and index' }));

    await waitFor(() => expect(requestsTo(fetchMock, 'POST', '/api/policies')).toHaveLength(1));
    const sent = requestsTo(fetchMock, 'POST', '/api/policies')[0]!.init.body as FormData;
    expect(sent.get('UploadedBy')).toBe('Policy Admin');
    expect(sent.get('PolicyFamily')).toBe('gold-plan');
    expect(sent.get('Version')).toBe('2024.1');
    expect((sent.get('File') as File).name).toBe('policy.md');
  });

  it('shows the server’s reason when an upload is refused', async () => {
    mockApi({
      'GET /api/policies': [], 'GET /api/ai/status': status,
      'POST /api/policies': () => jsonResponse({ title: 'Business rule violated', status: 409, detail: 'This exact file was already uploaded as ‘Gold Plan Policy’ (document 1).' }, 409),
    });
    const user = userEvent.setup();
    renderAt(<PoliciesPage />, '/policies');
    const form = await screen.findByRole('form', { name: 'Upload policy' });
    await user.upload(within(form).getByLabelText(/^File/), new File(['x'], 'dup.md'));
    await user.type(within(form).getByLabelText('Uploaded by'), 'Policy Admin');

    await user.click(within(form).getByRole('button', { name: 'Upload and index' }));

    expect(await screen.findByText(/already uploaded/)).toBeInTheDocument();
  });

  it('searches with the chosen method and shows per-stage scores', async () => {
    const fetchMock = mockApi({
      'GET /api/policies': [policyDoc()], 'GET /api/ai/status': status,
      'POST /api/policies/search': () => jsonResponse({
        query: 'dental waiting period', mode: 'KeywordOnly', keywordCandidates: 3, vectorCandidates: 0,
        passages: [{ rank: 1, chunkId: 9, documentId: 1, documentTitle: 'Gold Plan Policy', documentVersion: '2024.1', clauseRef: '5.2', heading: '5 Dental', sectionPath: 'Gold > 5 Dental', pageNumber: 1,
          text: '5.2 Waiting period. A waiting period of 90 days applies.', rerankScore: 0.91, fusedScore: 0.03, keywordScore: 8.25, keywordRank: 1, vectorScore: null, vectorRank: null }],
      }),
    });
    const user = userEvent.setup();
    renderAt(<PoliciesPage />, '/policies');
    await screen.findByText('Gold Plan Policy');

    await user.type(screen.getByLabelText('Question or keywords'), 'dental waiting period');
    await user.selectOptions(screen.getByLabelText('Method'), 'KeywordOnly');
    await user.click(screen.getByRole('button', { name: 'Search' }));

    expect(await screen.findByText(/clause 5.2/)).toBeInTheDocument();
    expect(screen.getByText(/relevance 0.91 · keyword 8.25 \(rank 1\) · vector — \(rank —\)/)).toBeInTheDocument();
    expect(bodyOf(requestsTo(fetchMock, 'POST', '/api/policies/search')[0]!)).toMatchObject({ query: 'dental waiting period', mode: 'KeywordOnly' });
  });
});
