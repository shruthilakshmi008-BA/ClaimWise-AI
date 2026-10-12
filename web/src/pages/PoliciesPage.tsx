import { useState, type FormEvent } from 'react';
import { ApiError, api } from '../api/client';
import type { Passage, PolicyChunk, PolicyDocument } from '../api/types';
import { Card, ErrorNotice, Field, Loading } from '../components/common';
import { bytes, dateOnly, dateTime } from '../lib/format';
import { useReviewerName } from '../lib/reviewer';
import { useAsync } from '../lib/useAsync';

export function PoliciesPage() {
  const docs = useAsync(() => api.listPolicies(), []);
  const status = useAsync(() => api.aiStatus(), []);
  const [message, setMessage] = useState<string>();
  const [error, setError] = useState<ApiError>();

  async function reindex() {
    setError(undefined);
    try {
      const r = await api.reindexPolicies();
      setMessage(`Re-indexed ${r.reindexedChunks} passages.`);
      docs.reload();
      status.reload();
    } catch (e) {
      setError(e instanceof ApiError ? e : undefined);
    }
  }

  return (
    <>
      <div className="page-title">
        <h1>Policy documents</h1>
        <button type="button" className="btn" onClick={reindex}>Re-index all</button>
      </div>
      <p className="muted">Upload fictional policy documents (.txt, .md, .pdf, .docx). Text is extracted, split into clause-level passages with source metadata, and indexed for hybrid search.</p>
      {message && <p className="notice notice-ok" role="status">{message}</p>}
      <ErrorNotice error={error} />

      <UploadForm onUploaded={() => { docs.reload(); status.reload(); }} />

      <Card title="Uploaded documents">
        {docs.loading && !docs.data && <Loading />}
        <ErrorNotice error={docs.error} onRetry={docs.reload} />
        {docs.data?.length === 0 && <p className="empty">No policy documents yet. Upload one above (sample files are in <code>samples/policies</code>).</p>}
        {docs.data && docs.data.length > 0 && (
          <ul className="doc-list">
            {docs.data.map((d) => <PolicyRow key={d.id} doc={d} onChanged={docs.reload} />)}
          </ul>
        )}
      </Card>

      <SearchPanel docs={docs.data ?? []} />
    </>
  );
}

function UploadForm({ onUploaded }: { onUploaded: () => void }) {
  const [reviewer] = useReviewerName();
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState('');
  const [family, setFamily] = useState('');
  const [version, setVersion] = useState('');
  const [effective, setEffective] = useState('');
  const [uploadedBy, setUploadedBy] = useState(reviewer);
  const [error, setError] = useState<ApiError>();
  const [busy, setBusy] = useState(false);

  async function submit(ev: FormEvent<HTMLFormElement>) {
    ev.preventDefault();
    if (!file) return;
    const form = new FormData();
    form.append('File', file);
    form.append('UploadedBy', uploadedBy.trim());
    if (title.trim()) form.append('Title', title.trim());
    if (family.trim()) form.append('PolicyFamily', family.trim());
    if (version.trim()) form.append('Version', version.trim());
    if (effective) form.append('EffectiveDate', effective);
    setBusy(true);
    setError(undefined);
    try {
      await api.uploadPolicy(form);
      ev.currentTarget.reset();
      setFile(null); setTitle(''); setFamily(''); setVersion(''); setEffective('');
      onUploaded();
    } catch (e) {
      setError(e instanceof ApiError ? e : undefined);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="Upload a policy document">
      <form onSubmit={submit} aria-label="Upload policy" className="form-grid">
        <Field label="File" htmlFor="pfile" hint=".txt, .md, .pdf or .docx, max 10 MB">
          <input id="pfile" type="file" accept=".txt,.md,.pdf,.docx" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
        </Field>
        <Field label="Uploaded by" htmlFor="pby" error={error?.fieldErrors.uploadedBy?.[0]}>
          <input id="pby" value={uploadedBy} onChange={(e) => setUploadedBy(e.target.value)} maxLength={100} />
        </Field>
        <Field label="Title (optional)" htmlFor="ptitle" hint="Defaults to the first heading"><input id="ptitle" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} /></Field>
        <Field label="Policy family (optional)" htmlFor="pfam" hint="Versions of one policy share a family, e.g. gold-plan. Conflicts are only checked within a family.">
          <input id="pfam" value={family} onChange={(e) => setFamily(e.target.value)} maxLength={100} />
        </Field>
        <Field label="Version (optional)" htmlFor="pver"><input id="pver" value={version} onChange={(e) => setVersion(e.target.value)} maxLength={50} /></Field>
        <Field label="Effective date (optional)" htmlFor="peff"><input id="peff" type="date" value={effective} onChange={(e) => setEffective(e.target.value)} /></Field>
        <div className="form-actions">
          <ErrorNotice error={error} />
          <button type="submit" className="btn btn-primary" disabled={!file || uploadedBy.trim().length < 2 || busy}>{busy ? 'Processing…' : 'Upload and index'}</button>
        </div>
      </form>
    </Card>
  );
}

function PolicyRow({ doc, onChanged }: { doc: PolicyDocument; onChanged: () => void }) {
  const [chunks, setChunks] = useState<PolicyChunk[]>();
  const [error, setError] = useState<ApiError>();

  async function toggle() {
    try { await api.setPolicyActive(doc.id, !doc.isActive); onChanged(); } catch (e) { setError(e instanceof ApiError ? e : undefined); }
  }
  async function showChunks() {
    if (chunks) return setChunks(undefined);
    try { setChunks(await api.policyChunks(doc.id)); } catch (e) { setError(e instanceof ApiError ? e : undefined); }
  }

  return (
    <li className={doc.isActive ? '' : 'inactive'}>
      <div className="doc-head">
        <div className="grow">
          <strong>{doc.title}</strong>{' '}
          {!doc.isActive && <span className="badge status-Rejected">Inactive</span>}
          <div className="muted small">
            {doc.fileName} · {bytes(doc.sizeBytes)} · {doc.pageCount} page{doc.pageCount === 1 ? '' : 's'} · {doc.chunkCount} passages
            {doc.policyFamily && <> · family <code>{doc.policyFamily}</code></>}
            {doc.version && <> · v{doc.version}</>}
            {doc.effectiveDate && <> · effective {dateOnly(doc.effectiveDate)}</>}
            {' '}· uploaded {dateTime(doc.uploadedAt)} by {doc.uploadedBy}
          </div>
        </div>
        <div className="btn-row">
          <button type="button" className="btn btn-small" onClick={showChunks} aria-expanded={!!chunks}>{chunks ? 'Hide passages' : 'View passages'}</button>
          <button type="button" className="btn btn-small" onClick={toggle}>{doc.isActive ? 'Deactivate' : 'Activate'}</button>
        </div>
      </div>
      <ErrorNotice error={error} />
      {chunks && (
        <ol className="chunk-list">
          {chunks.map((c) => (
            <li key={c.id}>
              <div className="muted small">{c.clauseRef ? `§${c.clauseRef} · ` : ''}{c.sectionPath} · page {c.pageNumber} · {c.wordCount} words</div>
              <div>{c.text}</div>
            </li>
          ))}
        </ol>
      )}
    </li>
  );
}

function SearchPanel({ docs }: { docs: PolicyDocument[] }) {
  const [query, setQuery] = useState('');
  const [mode, setMode] = useState('Hybrid');
  const [results, setResults] = useState<Passage[]>();
  const [error, setError] = useState<ApiError>();
  const [busy, setBusy] = useState(false);

  async function search(ev: FormEvent) {
    ev.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      setResults((await api.searchPolicies({ query: query.trim(), mode, topK: 6 })).passages);
    } catch (e) {
      setError(e instanceof ApiError ? e : undefined);
      setResults(undefined);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="Search the policies">
      <form onSubmit={search} role="search" aria-label="Search policies" className="inline-form">
        <div className="field grow">
          <label htmlFor="pq">Question or keywords</label>
          <input id="pq" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="e.g. dental waiting period" maxLength={500} />
        </div>
        <div className="field">
          <label htmlFor="pmode">Method</label>
          <select id="pmode" value={mode} onChange={(e) => setMode(e.target.value)}>
            <option value="Hybrid">Hybrid (keyword + vector + rerank)</option>
            <option value="KeywordOnly">Keyword only</option>
            <option value="VectorOnly">Vector only</option>
          </select>
        </div>
        <button type="submit" className="btn" disabled={query.trim().length < 3 || busy || docs.length === 0}>{busy ? 'Searching…' : 'Search'}</button>
      </form>
      <ErrorNotice error={error} />
      {results && results.length === 0 && <p className="empty">No matching passages.</p>}
      {results && results.length > 0 && (
        <ol className="passages">
          {results.map((p) => (
            <li key={p.chunkId}>
              <div><strong>{p.rank}.</strong> {p.documentTitle}{p.clauseRef ? ` · clause ${p.clauseRef}` : ''} · page {p.pageNumber}</div>
              <div className="muted small">
                relevance {p.rerankScore.toFixed(2)} · keyword {p.keywordScore?.toFixed(2) ?? '—'} (rank {p.keywordRank ?? '—'}) · vector {p.vectorScore?.toFixed(2) ?? '—'} (rank {p.vectorRank ?? '—'})
              </div>
              <blockquote>{p.text}</blockquote>
            </li>
          ))}
        </ol>
      )}
    </Card>
  );
}
