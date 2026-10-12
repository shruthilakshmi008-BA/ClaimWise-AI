import { useState } from 'react';
import { ApiError, api } from '../../api/client';
import type { AiSummary, ClaimDetail, ClaimStatus } from '../../api/types';
import { humanize } from '../../lib/format';
import { useReviewerName } from '../../lib/reviewer';
import { Card, ErrorNotice, Field } from '../common';

const NOTE_REQUIRED: ClaimStatus[] = ['Approved', 'Rejected', 'NeedsInformation'];
const DECISIONS: ClaimStatus[] = ['Approved', 'Rejected'];

/** Status changes are always made by a named person. The AI never appears as an actor here. */
export function WorkflowPanel({ claim, summaries, onChanged }: { claim: ClaimDetail; summaries: AiSummary[]; onChanged: () => void }) {
  const [reviewer, setReviewer] = useReviewerName();
  const [note, setNote] = useState('');
  const [error, setError] = useState<ApiError>();
  const [busy, setBusy] = useState<ClaimStatus>();
  const [localError, setLocalError] = useState<string>();

  const pending = summaries.filter((s) => s.review.state === 'PendingReview');
  const isFinal = claim.allowedNextStatuses.length === 0;

  async function move(target: ClaimStatus) {
    setLocalError(undefined);
    setError(undefined);
    if (reviewer.trim().length < 2) return setLocalError('Enter your name: every decision is recorded against a named reviewer.');
    if (NOTE_REQUIRED.includes(target) && !note.trim()) return setLocalError(`A note is required when moving a claim to ${humanize(target)}.`);
    setBusy(target);
    try {
      await api.changeStatus(claim.id, { newStatus: target, reviewerName: reviewer.trim(), note: note.trim() || undefined, expectedVersion: claim.version });
      setNote('');
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e : undefined);
      if (e instanceof ApiError && e.status === 409) onChanged(); // the claim may have changed underneath us
    } finally {
      setBusy(undefined);
    }
  }

  return (
    <Card title="Review & decision" id="workflow">
      {claim.reviewerName && (
        <p className="muted">
          Last action by <strong>{claim.reviewerName}</strong>
          {claim.reviewerNote ? <> — “{claim.reviewerNote}”</> : null}
        </p>
      )}
      {isFinal ? (
        <p className="notice notice-ok" role="status">This claim is {humanize(claim.status)}. Final claims cannot be changed.</p>
      ) : (
        <>
          <p className="muted">Decisions are made by a person. AI summaries are evidence only and never change a claim.</p>
          {pending.length > 0 && (
            <p className="notice notice-warn" role="status">
              {pending.length} AI summar{pending.length === 1 ? 'y is' : 'ies are'} awaiting your review. Accept or reject {pending.length === 1 ? 'it' : 'them'} below before approving or rejecting this claim.
            </p>
          )}
          <div className="form-grid">
            <Field label="Your name (reviewer)" htmlFor="reviewer">
              <input id="reviewer" value={reviewer} onChange={(e) => setReviewer(e.target.value)} maxLength={100} autoComplete="name" />
            </Field>
            <Field label="Note" htmlFor="note" hint={`Required for ${['Approved', 'Rejected', 'Needs information'].join(', ')}`}>
              <textarea id="note" rows={3} value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} />
            </Field>
          </div>
          <div className="btn-row">
            {claim.allowedNextStatuses.map((s) => {
              const blocked = DECISIONS.includes(s) && pending.length > 0;
              return (
                <button key={s} type="button" disabled={busy !== undefined || blocked}
                  className={`btn ${s === 'Approved' ? 'btn-success' : s === 'Rejected' ? 'btn-danger' : ''}`}
                  title={blocked ? 'Review the AI summary first' : undefined} onClick={() => move(s)}>
                  {busy === s ? 'Saving…' : `Move to ${humanize(s)}`}
                </button>
              );
            })}
          </div>
          <ErrorNotice error={localError ?? error} />
        </>
      )}
    </Card>
  );
}
