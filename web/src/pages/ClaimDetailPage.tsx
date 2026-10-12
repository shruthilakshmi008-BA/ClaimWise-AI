import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { AiSummaryPanel } from '../components/claim/AiSummaryPanel';
import { AuditTimeline } from '../components/claim/AuditTimeline';
import { DocumentsPanel } from '../components/claim/DocumentsPanel';
import { WorkflowPanel } from '../components/claim/WorkflowPanel';
import { Card, ErrorNotice, Loading, StatusBadge } from '../components/common';
import { dateOnly, dateTime, money } from '../lib/format';
import { useAsync } from '../lib/useAsync';

export function ClaimDetailPage() {
  const id = Number(useParams().id);
  const claim = useAsync(() => api.getClaim(id), [id]);
  const audit = useAsync(() => api.audit(id), [id]);
  const summaries = useAsync(() => api.listSummaries(id), [id]);

  const refresh = () => {
    claim.reload();
    audit.reload();
    summaries.reload();
  };

  if (!Number.isInteger(id)) return <p>Invalid claim id.</p>;
  if (claim.loading && !claim.data) return <Loading label="Loading claim…" />;
  if (claim.error && !claim.data) {
    return (
      <>
        <p><Link to="/claims">← All claims</Link></p>
        <ErrorNotice error={claim.error} onRetry={claim.reload} />
      </>
    );
  }
  const c = claim.data!;

  return (
    <>
      <p><Link to="/claims">← All claims</Link></p>
      <div className="page-title">
        <h1>{c.claimNumber} <StatusBadge status={c.status} /></h1>
      </div>

      <Card title="Claim details">
        <dl className="facts">
          <div><dt>Patient</dt><dd>{c.patientName}</dd></div>
          <div><dt>Member ID</dt><dd>{c.memberId}</dd></div>
          <div><dt>Policy number</dt><dd>{c.policyNumber}</dd></div>
          <div><dt>Provider</dt><dd>{c.providerName}</dd></div>
          <div><dt>Claim type</dt><dd>{c.claimType}</dd></div>
          <div><dt>Amount claimed</dt><dd>{money(c.amountClaimed)}</dd></div>
          <div><dt>Service date</dt><dd>{dateOnly(c.serviceDate)}</dd></div>
          <div><dt>Diagnosis code</dt><dd>{c.diagnosisCode ?? <span className="muted">not provided</span>}</dd></div>
          <div><dt>Procedure code</dt><dd>{c.procedureCode ?? <span className="muted">not provided</span>}</dd></div>
          <div><dt>Submitted</dt><dd>{dateTime(c.submittedAt)}</dd></div>
        </dl>
      </Card>

      <ErrorNotice error={claim.error} onRetry={claim.reload} />

      <div className="grid-2 align-start">
        <DocumentsPanel claim={c} onChanged={refresh} />
        <WorkflowPanel claim={c} summaries={summaries.data ?? []} onChanged={refresh} />
      </div>

      <AiSummaryPanel claimId={c.id} isFinal={c.allowedNextStatuses.length === 0} summaries={summaries.data ?? []} onChanged={refresh} />
      <ErrorNotice error={summaries.error} onRetry={summaries.reload} />

      {audit.data && <AuditTimeline entries={audit.data} />}
    </>
  );
}
