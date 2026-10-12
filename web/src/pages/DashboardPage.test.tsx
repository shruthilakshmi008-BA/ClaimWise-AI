import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DashboardPage } from './DashboardPage';
import { dashboard, mockApi, renderAt } from '../test/utils';

const aiStatus = {
  embeddingProvider: 'Local', embeddingModel: 'local-hashing-v1-512', summaryGenerator: 'extractive', summaryModel: 'extractive-v1',
  activeDocuments: 4, indexedChunks: 90, staleChunks: 0, summariesUseLanguageModel: false,
};

describe('DashboardPage', () => {
  it('shows real counts per status, the total and incomplete claims', async () => {
    mockApi({ 'GET /api/claims/summary': dashboard, 'GET /api/ai/status': aiStatus });
    renderAt(<DashboardPage />, '/');

    const tiles = await screen.findByLabelText('Claims by status');
    expect(within(tiles).getByText('8')).toBeInTheDocument();
    expect(within(tiles).getByRole('link', { name: /4\s*Submitted/ })).toHaveAttribute('href', '/claims?status=Submitted');
    expect(within(tiles).getByRole('link', { name: /Needs information/ })).toBeInTheDocument();
    expect(screen.getByText(/183,171\.25/)).toBeInTheDocument();
  });

  it('is honest about how summaries are produced and warns about stale embeddings', async () => {
    mockApi({ 'GET /api/claims/summary': dashboard, 'GET /api/ai/status': { ...aiStatus, staleChunks: 12 } });
    renderAt(<DashboardPage />, '/');

    expect(await screen.findByText('Extractive quoting — no language model')).toBeInTheDocument();
    expect(screen.getByText(/12 passages use a different embedding model/)).toBeInTheDocument();
  });
});
