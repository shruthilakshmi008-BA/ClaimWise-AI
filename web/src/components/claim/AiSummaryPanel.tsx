import { useState, type FormEvent } from 'react';
import { ApiError, api } from '../../api/client';
import type { AiSummary, SummaryPassage, SummaryStatement } from '../../api/types';
import { dateTime, percent, splitAroundQuote } from '../../lib/format';
import { useReviewerName } from '../../lib/reviewer';
import { useAsync } from '../../lib/useAsync';
import { Card, ErrorNotice, Field, ReviewBadge, SummaryStatusBadge } from '../common';

interface Props {
  claimId: number;
  isFinal: boolean;
  summaries: AiSummary[];
  onChanged: () => void;
}

export function AiSummaryPanel({ claimId, isFinal, summaries, onChanged }: Props) {
  const [reviewer] = useReviewerName();
  const [question, setQuestion] = useState('');
  const [error, setError] = useState<ApiError>();
  const [busy, setBusy] = useState(false);
  const [scope, setScope] = useState<number[]>([]);
  const policies = useAsync(() => api.listPolicies(), []);
  const activePolicies = (policies.data ?? []).filter((p) => p.isActive);

  async function generate(ev: FormEvent) {
    ev.preventDefault();
    setError(undefined);
    if (reviewer.trim().length < 2) return setError(new ApiError(0, 'Name needed', 'Enter your name under “Review & decision” first: summaries are logged against a named person.'));
    setBusy(true);
    try {
      await api.generateSummary(claimId, { question: question.trim() || undefined, requestedBy: reviewer.trim(), documentIds: scope.length ? scope : undefined });
      setQuestion('');
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e : undefined);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="AI policy evidence" id="ai">
      <p className="notice notice-info">
        Retrieves passages from the uploaded policy documents and quotes them with citations. It <strong>cannot approve or deny</strong> a claim,
        and says “insufficient evidence” rather than guessing.
      </p>

      {!isFinal && (
        <form onSubmit={generate} aria-label="Generate AI summary" className="stack">
          <Field label="Question (optional)" htmlFor="aiQuestion" hint="Leave empty to ask about benefits, limits, co-payment, exclusions and required documents for this claim type.">
            <input id="aiQuestion" value={question} onChange={(e) => setQuestion(e.target.value)} maxLength={500} />
          </Field>
          {activePolicies.length > 1 && (
            <fieldset className="scope">
              <legend>Policy documents to consult <span className="muted small">(none selected = all active)</span></legend>
              {activePolicies.map((p) => (
                <label key={p.id} className="check">
                  <input type="checkbox" checked={scope.includes(p.id)}
                    onChange={(e) => setScope((s) => (e.target.checked ? [...s, p.id] : s.filter((x) => x !== p.id)))} />
                  {p.title}{p.version ? ` (v${p.version})` : ''}
                </label>
              ))}
            </fieldset>
          )}
          <div><button type="submit" className="btn btn-primary" disabled={busy}>{busy ? 'Retrieving evidence…' : 'Find policy evidence'}</button></div>
        </form>
      )}
      <ErrorNotice error={error} />

      {summaries.length === 0 && <p className="muted">No AI summaries yet.</p>}
      {summaries.map((s) => <SummaryCard key={s.id} claimId={claimId} summary={s} onChanged={onChanged} />)}
    </Card>
  );
}

function generatorLabel(s: AiSummary) {
  return s.usedLanguageModel ? `Language model: ${s.model}` : 'Extractive quoting (no language model)';
}

export function SummaryCard({ claimId, summary: s, onChanged }: { claimId: number; summary: AiSummary; onChanged: () => void }) {
  return (
    <article className="summary" aria-label={`AI summary ${s.id}`}>
      <header className="summary-head">
        <div>
          <strong>Summary #{s.id}</strong> <span className="muted small">· {dateTime(s.generatedAt)} · requested by {s.requestedBy}</span>
        </div>
        <div className="badges"><SummaryStatusBadge status={s.status} /><ReviewBadge state={s.review.state} /></div>
      </header>
      <p className="muted small">Q: {s.question} · {generatorLabel(s)}</p>
      <p className="ai-label">AI-assisted — verify every citation. Not a coverage decision.</p>

      {s.status === 'Failed' && <div className="notice notice-error" role="alert">The summary could not be produced: {s.failureReason}. No findings were generated.</div>}

      {s.status === 'Abstained' && (
        <div className="notice notice-warn" role="status">
          <strong>Insufficient evidence — no findings shown.</strong>
          <div>{s.abstainReason}</div>
          {s.evidence.missingTerms.length > 0 && <div className="small">Question terms not found in the policies: {s.evidence.missingTerms.join(', ')}.</div>}
        </div>
      )}

      {s.conflicts.length > 0 && (
        <div className="notice notice-conflict" role="alert">
          <strong>Conflicting policy wording</strong>
          <ul>{s.conflicts.map((c, i) => <li key={i}>{c.description}</li>)}</ul>
          <div className="small">The system does not choose a side. A reviewer must resolve this.</div>
        </div>
      )}
      {s.warnings.map((w, i) => <div key={i} className="notice notice-warn" role="status">{w}</div>)}

      {s.statements.length > 0 && (
        <ol className="statements">
          {s.statements.map((st) => <Statement key={st.ordinal} statement={st} passages={s.passages} />)}
        </ol>
      )}

      <details>
        <summary>Evidence details · {s.passages.length} passage{s.passages.length === 1 ? '' : 's'} retrieved · {percent(s.evidence.coverage)} of question terms covered</summary>
        <p className="small muted">
          Evidence assessment: <strong>{s.evidence.verdict}</strong> (top relevance {s.evidence.topScore.toFixed(2)}, {s.evidence.supportingPassages} supporting passage(s)).
          {s.evidence.reasons.length > 0 && <> {s.evidence.reasons.join(' ')}</>}
        </p>
        <ul className="passages">
          {s.passages.map((p) => (
            <li key={p.label}>
              <div><strong>[{p.label}]</strong> {passageTitle(p)}</div>
              <div className="muted small">
                relevance {p.rerankScore.toFixed(2)}
                {p.keywordScore != null && <> · keyword {p.keywordScore.toFixed(2)}</>}
                {p.vectorScore != null && <> · vector {p.vectorScore.toFixed(2)}</>}
              </div>
              <blockquote>{p.text}</blockquote>
            </li>
          ))}
        </ul>
      </details>

      {s.rejectedStatements.length > 0 && (
        <details>
          <summary>{s.rejectedStatements.length} generated statement(s) removed — citations could not be verified</summary>
          <ul className="small">{s.rejectedStatements.map((r, i) => <li key={i}>“{r.text}” — {r.reason}</li>)}</ul>
        </details>
      )}

      <ReviewControls claimId={claimId} summary={s} onChanged={onChanged} />
    </article>
  );
}

const passageTitle = (p: SummaryPassage) =>
  [p.documentTitle, p.clauseRef ? `clause ${p.clauseRef}` : null, `page ${p.pageNumber}`].filter(Boolean).join(' · ');

function Statement({ statement, passages }: { statement: SummaryStatement; passages: SummaryPassage[] }) {
  const [open, setOpen] = useState<string | null>(null);
  return (
    <li className={statement.origin === 'System' ? 'system' : undefined}>
      <p>
        {statement.origin === 'System' && <span className="badge badge-system">System notice</span>} {statement.text}{' '}
        {statement.citations.map((c) => (
          <button key={c.passage} type="button" className="cite" aria-expanded={open === c.passage}
            onClick={() => setOpen(open === c.passage ? null : c.passage)}>
            [{c.passage}]
          </button>
        ))}
      </p>
      {statement.citations.filter((c) => c.passage === open).map((c) => {
        const passage = passages.find((p) => p.label === c.passage);
        if (!passage) return null;
        const parts = splitAroundQuote(passage.text, c.quote);
        return (
          <blockquote key={c.passage} className="source" aria-label={`Source ${c.passage}`}>
            <cite>{passageTitle(passage)}</cite>
            {parts ? <>{parts.before}<mark>{parts.match}</mark>{parts.after}</> : passage.text}
          </blockquote>
        );
      })}
    </li>
  );
}

function ReviewControls({ claimId, summary: s, onChanged }: { claimId: number; summary: AiSummary; onChanged: () => void }) {
  const [reviewer, setReviewer] = useReviewerName();
  const [note, setNote] = useState('');
  const [error, setError] = useState<ApiError | string>();
  const [busy, setBusy] = useState(false);

  if (s.review.state === 'NotRequired') return null;
  if (s.review.state !== 'PendingReview') {
    return (
      <p className="review-result">
        {s.review.state === 'Accepted' ? 'Accepted' : 'Rejected'} by <strong>{s.review.reviewedBy}</strong> on {s.review.reviewedAt ? dateTime(s.review.reviewedAt) : ''}
        {s.review.note ? <> — “{s.review.note}”</> : null}
      </p>
    );
  }

  async function submit(decision: 'Accepted' | 'Rejected') {
    setError(undefined);
    if (reviewer.trim().length < 2) return setError('Enter your name to record the review.');
    if (decision === 'Rejected' && !note.trim()) return setError('A note is required when rejecting an AI summary.');
    setBusy(true);
    try {
      await api.reviewSummary(claimId, s.id, { decision, reviewerName: reviewer.trim(), note: note.trim() || undefined });
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e : undefined);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="review-box" role="group" aria-label={`Review summary ${s.id}`}>
      <strong>Your review</strong>
      <p className="muted small">Check each citation against the policy text, then accept or reject this summary. Required before the claim can be approved or rejected.</p>
      <div className="form-grid">
        <Field label="Reviewer name" htmlFor={`rev-name-${s.id}`}>
          <input id={`rev-name-${s.id}`} value={reviewer} onChange={(e) => setReviewer(e.target.value)} maxLength={100} />
        </Field>
        <Field label="Note (required to reject)" htmlFor={`rev-note-${s.id}`}>
          <input id={`rev-note-${s.id}`} value={note} onChange={(e) => setNote(e.target.value)} maxLength={1000} />
        </Field>
      </div>
      <div className="btn-row">
        <button type="button" className="btn btn-success" disabled={busy} onClick={() => submit('Accepted')}>Accept summary</button>
        <button type="button" className="btn btn-danger" disabled={busy} onClick={() => submit('Rejected')}>Reject summary</button>
      </div>
      <ErrorNotice error={error} />
    </div>
  );
}
