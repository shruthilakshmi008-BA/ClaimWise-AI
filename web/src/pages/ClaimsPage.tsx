import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { CLAIM_STATUSES, CLAIM_TYPES, type ClaimListParams, type ClaimStatus, type ClaimType } from '../api/types';
import { ErrorNotice, Loading, Pagination, StatusBadge } from '../components/common';
import { dateOnly, humanize, money } from '../lib/format';
import { useAsync } from '../lib/useAsync';

const SORTS: { value: NonNullable<ClaimListParams['sortBy']>; label: string }[] = [
  { value: 'submittedAt', label: 'Submitted date' },
  { value: 'serviceDate', label: 'Service date' },
  { value: 'amount', label: 'Amount' },
  { value: 'claimNumber', label: 'Claim number' },
  { value: 'status', label: 'Status (workflow order)' },
];

const PAGE_SIZE = 10;

export function ClaimsPage() {
  const [params, setParams] = useSearchParams();
  const status = (params.get('status') ?? '') as ClaimStatus | '';
  const claimType = (params.get('type') ?? '') as ClaimType | '';
  const search = params.get('q') ?? '';
  const sortBy = (params.get('sort') ?? 'submittedAt') as NonNullable<ClaimListParams['sortBy']>;
  const sortDir = (params.get('dir') ?? 'desc') as 'asc' | 'desc';
  const page = Math.max(1, Number(params.get('page') ?? 1) || 1);

  // The search box is local state so typing is instant; the URL (and the request) follows after a short pause.
  const [searchText, setSearchText] = useState(search);
  useEffect(() => setSearchText(search), [search]);
  useEffect(() => {
    if (searchText === search) return;
    const t = setTimeout(() => update({ q: searchText, page: '' }), 300);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchText]);

  function update(changes: Record<string, string>) {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    setParams(next, { replace: true });
  }

  const result = useAsync(
    () => api.listClaims({ status, claimType, search, sortBy, sortDir, page, pageSize: PAGE_SIZE }),
    [status, claimType, search, sortBy, sortDir, page],
  );

  return (
    <>
      <div className="page-title">
        <h1>Claims</h1>
        <Link to="/claims/new" className="btn btn-primary">New claim</Link>
      </div>

      <form className="filters" role="search" onSubmit={(e) => e.preventDefault()} aria-label="Filter claims">
        <div className="field grow">
          <label htmlFor="q">Search</label>
          <input id="q" type="search" placeholder="Claim no., patient, member, policy, provider, code" value={searchText}
            onChange={(e) => setSearchText(e.target.value)} maxLength={100} />
        </div>
        <div className="field">
          <label htmlFor="status">Status</label>
          <select id="status" value={status} onChange={(e) => update({ status: e.target.value, page: '' })}>
            <option value="">All statuses</option>
            {CLAIM_STATUSES.map((s) => <option key={s} value={s}>{humanize(s)}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="type">Claim type</label>
          <select id="type" value={claimType} onChange={(e) => update({ type: e.target.value, page: '' })}>
            <option value="">All types</option>
            {CLAIM_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="sort">Sort by</label>
          <select id="sort" value={sortBy} onChange={(e) => update({ sort: e.target.value, page: '' })}>
            {SORTS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="dir">Direction</label>
          <select id="dir" value={sortDir} onChange={(e) => update({ dir: e.target.value, page: '' })}>
            <option value="desc">Descending</option>
            <option value="asc">Ascending</option>
          </select>
        </div>
      </form>

      <ErrorNotice error={result.error} onRetry={result.reload} />
      {result.loading && !result.data && <Loading />}

      {result.data && result.data.items.length === 0 && (
        <p className="empty">No claims match these filters.</p>
      )}

      {result.data && result.data.items.length > 0 && (
        <div className="table-wrap" aria-busy={result.loading}>
          <table className="responsive-table">
            <thead>
              <tr>
                <th scope="col">Claim</th><th scope="col">Patient</th><th scope="col">Provider</th><th scope="col">Type</th>
                <th scope="col" className="num">Amount</th><th scope="col">Service date</th><th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {result.data.items.map((c) => (
                <tr key={c.id}>
                  <td data-label="Claim"><Link to={`/claims/${c.id}`}>{c.claimNumber}</Link></td>
                  <td data-label="Patient">{c.patientName}</td>
                  <td data-label="Provider">{c.providerName}</td>
                  <td data-label="Type">{c.claimType}</td>
                  <td data-label="Amount" className="num">{money(c.amountClaimed)}</td>
                  <td data-label="Service date">{dateOnly(c.serviceDate)}</td>
                  <td data-label="Status"><StatusBadge status={c.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {result.data && (
        <Pagination page={result.data.page} totalPages={result.data.totalPages} totalCount={result.data.totalCount}
          onPage={(p) => update({ page: p > 1 ? String(p) : '' })} />
      )}
    </>
  );
}
