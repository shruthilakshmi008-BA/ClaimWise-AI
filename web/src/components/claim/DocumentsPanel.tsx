import { useState, type FormEvent } from 'react';
import { ApiError, api } from '../../api/client';
import { DOCUMENT_TYPES, type ClaimDetail, type DocumentType } from '../../api/types';
import { bytes, dateTime, humanize } from '../../lib/format';
import { Card, ErrorNotice } from '../common';

export function DocumentsPanel({ claim, onChanged }: { claim: ClaimDetail; onChanged: () => void }) {
  const have = new Set(claim.documents.map((d) => d.documentType));
  const options = DOCUMENT_TYPES.filter((t) => !have.has(t));
  const [type, setType] = useState<DocumentType | ''>('');
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<ApiError>();
  const [busy, setBusy] = useState(false);
  const isFinal = claim.allowedNextStatuses.length === 0;
  const chosen = type || options.find((t) => claim.validation.missingDocuments.includes(t)) || options[0] || '';

  async function upload(ev: FormEvent) {
    ev.preventDefault();
    if (!chosen || !file) return;
    setBusy(true);
    setError(undefined);
    try {
      await api.uploadClaimDocument(claim.id, chosen, file);
      setFile(null);
      setType('');
      (ev.target as HTMLFormElement).reset();
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e : undefined);
    } finally {
      setBusy(false);
    }
  }

  const { validation } = claim;
  return (
    <Card title="Documents & completeness" id="documents">
      {validation.isComplete ? (
        <p className="notice notice-ok" role="status">Submission is complete: all required documents and fields are present.</p>
      ) : (
        <div className="notice notice-warn" role="status">
          <strong>Incomplete submission.</strong>
          {validation.missingDocuments.length > 0 && <div>Missing documents: {validation.missingDocuments.map(humanize).join(', ')}.</div>}
          {validation.missingFields.length > 0 && <div>Missing fields: {validation.missingFields.map(humanize).join(', ')}.</div>}
        </div>
      )}

      <ul className="checklist" aria-label="Required documents">
        {validation.requiredDocuments.map((t) => {
          const doc = claim.documents.find((d) => d.documentType === t);
          return (
            <li key={t} className={doc ? 'done' : 'missing'}>
              <span aria-hidden="true">{doc ? '✓' : '○'}</span>
              <span className="grow">{humanize(t)}</span>
              {doc ? (
                <span className="muted small">
                  {doc.hasFile ? <a href={api.claimDocumentUrl(claim.id, doc.id)}>{doc.fileName}</a> : doc.fileName}
                  {doc.sizeBytes != null && ` · ${bytes(doc.sizeBytes)}`} · {dateTime(doc.receivedAt)}
                </span>
              ) : (
                <span className="muted small">missing</span>
              )}
            </li>
          );
        })}
        {claim.documents.filter((d) => !validation.requiredDocuments.includes(d.documentType)).map((d) => (
          <li key={d.id} className="done">
            <span aria-hidden="true">✓</span><span className="grow">{humanize(d.documentType)} (additional)</span>
            <span className="muted small">{d.fileName}</span>
          </li>
        ))}
      </ul>

      {!isFinal && options.length > 0 && (
        <form onSubmit={upload} className="inline-form" aria-label="Upload document">
          <div className="field">
            <label htmlFor="docType">Document type</label>
            <select id="docType" value={chosen} onChange={(e) => setType(e.target.value as DocumentType)}>
              {options.map((t) => <option key={t} value={t}>{humanize(t)}</option>)}
            </select>
          </div>
          <div className="field grow">
            <label htmlFor="docFile">File (pdf, png, jpg, docx, txt — max 10 MB)</label>
            <input id="docFile" type="file" accept=".pdf,.png,.jpg,.jpeg,.docx,.txt" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </div>
          <button type="submit" className="btn" disabled={!file || busy}>{busy ? 'Uploading…' : 'Upload'}</button>
        </form>
      )}
      <ErrorNotice error={error} />
    </Card>
  );
}
