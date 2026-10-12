import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { CLAIM_STATUSES } from '../api/types';
import { Card, ErrorNotice, Loading } from '../components/common';
import { humanize, money } from '../lib/format';
import { useAsync } from '../lib/useAsync';

export function DashboardPage() {
  const summary = useAsync(() => api.dashboard(), []);
  const ai = useAsync(() => api.aiStatus(), []);

  return (
    <>
      <h1>Dashboard</h1>
      {summary.loading && !summary.data && <Loading />}
      <ErrorNotice error={summary.error} onRetry={summary.reload} />
      {summary.data && (
        <>
          <div className="tiles" aria-label="Claims by status">
            <div className="tile">
              <span className="tile-value">{summary.data.totalClaims}</span>
              <span className="tile-label">Total claims</span>
            </div>
            {CLAIM_STATUSES.map((s) => (
              <Link key={s} to={`/claims?status=${s}`} className="tile tile-link">
                <span className="tile-value">{summary.data!.byStatus[s] ?? 0}</span>
                <span className="tile-label">{humanize(s)}</span>
              </Link>
            ))}
          </div>

          <div className="grid-2">
            <Card title="Total amount claimed">
              <p className="big-number">{money(summary.data.totalAmountClaimed)}</p>
            </Card>
            <Card title="Open claims with incomplete submission">
              <p className="big-number">{summary.data.openClaimsWithIncompleteSubmission}</p>
              <p className="muted">Open claims still missing a required document or field.</p>
            </Card>
          </div>
        </>
      )}

      <Card title="AI & retrieval status">
        {ai.loading && !ai.data && <Loading />}
        <ErrorNotice error={ai.error} onRetry={ai.reload} />
        {ai.data && (
          <dl className="facts">
            <div><dt>Summaries written by</dt><dd>{ai.data.summariesUseLanguageModel ? `Language model (${ai.data.summaryModel})` : 'Extractive quoting — no language model'}</dd></div>
            <div><dt>Embeddings</dt><dd>{ai.data.embeddingModel}</dd></div>
            <div><dt>Policy documents / passages indexed</dt><dd>{ai.data.activeDocuments} / {ai.data.indexedChunks}</dd></div>
            {ai.data.staleChunks > 0 && (
              <div><dt>Needs re-index</dt><dd className="warn">{ai.data.staleChunks} passages use a different embedding model. Re-index on the Policies page.</dd></div>
            )}
          </dl>
        )}
      </Card>
    </>
  );
}
