import type { ReactNode } from 'react';
import { ApiError } from '../api/client';
import type { AiSummaryStatus, ClaimStatus, SummaryReviewState } from '../api/types';
import { humanize } from '../lib/format';

export function StatusBadge({ status }: { status: ClaimStatus }) {
  return <span className={`badge status-${status}`}>{humanize(status)}</span>;
}

const summaryLabel: Record<AiSummaryStatus, string> = { Answered: 'Findings', Abstained: 'Abstained', Failed: 'Provider failed' };

export function SummaryStatusBadge({ status }: { status: AiSummaryStatus }) {
  return <span className={`badge summary-${status}`}>{summaryLabel[status]}</span>;
}

const reviewLabel: Record<SummaryReviewState, string> = {
  NotRequired: 'No review needed',
  PendingReview: 'Awaiting human review',
  Accepted: 'Accepted by reviewer',
  Rejected: 'Rejected by reviewer',
};

export function ReviewBadge({ state }: { state: SummaryReviewState }) {
  return <span className={`badge review-${state}`}>{reviewLabel[state]}</span>;
}

export function ErrorNotice({ error, onRetry }: { error: ApiError | string | undefined; onRetry?: () => void }) {
  if (!error) return null;
  const message = typeof error === 'string' ? error : error.message;
  return (
    <div className="notice notice-error" role="alert">
      <span>{message}</span>
      {onRetry && (
        <button type="button" className="btn btn-small" onClick={onRetry}>
          Retry
        </button>
      )}
    </div>
  );
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <p className="muted" role="status" aria-live="polite">
      {label}
    </p>
  );
}

export function Card({ title, actions, children, id }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; id?: string }) {
  return (
    <section className="card" id={id} aria-labelledby={id ? `${id}-title` : undefined}>
      {(title || actions) && (
        <header className="card-header">
          {title && <h2 id={id ? `${id}-title` : undefined}>{title}</h2>}
          {actions && <div className="card-actions">{actions}</div>}
        </header>
      )}
      {children}
    </section>
  );
}

export function Field({
  label, error, hint, children, htmlFor,
}: { label: string; error?: string; hint?: string; children: ReactNode; htmlFor: string }) {
  return (
    <div className={`field${error ? ' has-error' : ''}`}>
      <label htmlFor={htmlFor}>{label}</label>
      {children}
      {hint && !error && <small className="muted">{hint}</small>}
      {error && (
        <small className="field-error" role="alert">
          {error}
        </small>
      )}
    </div>
  );
}

export function Pagination({
  page, totalPages, totalCount, onPage,
}: { page: number; totalPages: number; totalCount: number; onPage: (p: number) => void }) {
  if (totalCount === 0) return null;
  return (
    <nav className="pagination" aria-label="Pagination">
      <span className="muted">
        Page {page} of {Math.max(totalPages, 1)} · {totalCount} claim{totalCount === 1 ? '' : 's'}
      </span>
      <div>
        <button type="button" className="btn btn-small" disabled={page <= 1} onClick={() => onPage(page - 1)}>
          ← Previous
        </button>
        <button type="button" className="btn btn-small" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>
          Next →
        </button>
      </div>
    </nav>
  );
}
