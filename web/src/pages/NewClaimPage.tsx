import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError, api } from '../api/client';
import { CLAIM_TYPES, type ClaimType, type CreateClaimInput } from '../api/types';
import { Card, ErrorNotice, Field } from '../components/common';

type FormState = {
  patientName: string; memberId: string; policyNumber: string; providerName: string; claimType: ClaimType | '';
  diagnosisCode: string; procedureCode: string; amountClaimed: string; serviceDate: string;
};

const empty: FormState = {
  patientName: '', memberId: '', policyNumber: '', providerName: '', claimType: '',
  diagnosisCode: '', procedureCode: '', amountClaimed: '', serviceDate: '',
};

const idPattern = /^[A-Za-z0-9-]{5,20}$/;

/** Mirrors the server rules so mistakes are caught immediately; the server remains the authority. */
export function validate(f: FormState, today = new Date()): Partial<Record<keyof FormState, string>> {
  const e: Partial<Record<keyof FormState, string>> = {};
  if (f.patientName.trim().length < 2) e.patientName = 'Enter the patient name (at least 2 characters).';
  if (!idPattern.test(f.memberId.trim())) e.memberId = 'Member ID must be 5–20 letters, digits or hyphens.';
  if (!idPattern.test(f.policyNumber.trim())) e.policyNumber = 'Policy number must be 5–20 letters, digits or hyphens.';
  if (f.providerName.trim().length < 2) e.providerName = 'Enter the provider name.';
  if (!f.claimType) e.claimType = 'Choose a claim type.';
  if (f.diagnosisCode.trim().length > 10) e.diagnosisCode = 'At most 10 characters.';
  if (f.procedureCode.trim().length > 10) e.procedureCode = 'At most 10 characters.';
  const amount = Number(f.amountClaimed);
  if (!f.amountClaimed || !Number.isFinite(amount) || amount < 0.01 || amount > 10_000_000) e.amountClaimed = 'Amount must be between 0.01 and 10,000,000.';
  else if (Math.round(amount * 100) / 100 !== amount) e.amountClaimed = 'At most 2 decimal places.';
  if (!f.serviceDate) e.serviceDate = 'Enter the service date.';
  else if (f.serviceDate > today.toISOString().slice(0, 10)) e.serviceDate = 'The service date cannot be in the future.';
  return e;
}

export function NewClaimPage() {
  const navigate = useNavigate();
  const [form, setForm] = useState<FormState>(empty);
  const [touched, setTouched] = useState(false);
  const [serverErrors, setServerErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<ApiError>();
  const [saving, setSaving] = useState(false);

  const clientErrors = touched ? validate(form) : {};
  const fieldError = (k: keyof FormState) => clientErrors[k] ?? serverErrors[k]?.[0];
  const set = (k: keyof FormState) => (e: { target: { value: string } }) => {
    setForm((f) => ({ ...f, [k]: e.target.value }));
    setServerErrors((s) => ({ ...s, [k]: [] }));
  };

  async function submit(ev: FormEvent) {
    ev.preventDefault();
    setTouched(true);
    setError(undefined);
    if (Object.keys(validate(form)).length > 0) return;
    setSaving(true);
    try {
      const input: CreateClaimInput = {
        patientName: form.patientName.trim(), memberId: form.memberId.trim(), policyNumber: form.policyNumber.trim(),
        providerName: form.providerName.trim(), claimType: form.claimType as ClaimType,
        diagnosisCode: form.diagnosisCode.trim() || undefined, procedureCode: form.procedureCode.trim() || undefined,
        amountClaimed: Number(form.amountClaimed), serviceDate: form.serviceDate,
      };
      const created = await api.createClaim(input);
      navigate(`/claims/${created.id}`);
    } catch (e) {
      if (e instanceof ApiError) {
        setServerErrors(e.fieldErrors);
        setError(e);
      }
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <h1>New claim</h1>
      <Card>
        <form onSubmit={submit} noValidate aria-label="New claim" className="form-grid">
          <Field label="Patient name" htmlFor="patientName" error={fieldError('patientName')}>
            <input id="patientName" value={form.patientName} onChange={set('patientName')} maxLength={100} autoComplete="off" />
          </Field>
          <Field label="Claim type" htmlFor="claimType" error={fieldError('claimType')}>
            <select id="claimType" value={form.claimType} onChange={set('claimType')}>
              <option value="">Select…</option>
              {CLAIM_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          </Field>
          <Field label="Member ID" htmlFor="memberId" error={fieldError('memberId')} hint="e.g. MBR-900001">
            <input id="memberId" value={form.memberId} onChange={set('memberId')} maxLength={20} autoComplete="off" />
          </Field>
          <Field label="Policy number" htmlFor="policyNumber" error={fieldError('policyNumber')} hint="e.g. POL-GOLD-01">
            <input id="policyNumber" value={form.policyNumber} onChange={set('policyNumber')} maxLength={20} autoComplete="off" />
          </Field>
          <Field label="Provider" htmlFor="providerName" error={fieldError('providerName')}>
            <input id="providerName" value={form.providerName} onChange={set('providerName')} maxLength={150} autoComplete="off" />
          </Field>
          <Field label="Service date" htmlFor="serviceDate" error={fieldError('serviceDate')}>
            <input id="serviceDate" type="date" value={form.serviceDate} onChange={set('serviceDate')} />
          </Field>
          <Field label="Diagnosis code" htmlFor="diagnosisCode" error={fieldError('diagnosisCode')} hint="Required for outpatient and inpatient claims">
            <input id="diagnosisCode" value={form.diagnosisCode} onChange={set('diagnosisCode')} maxLength={10} autoComplete="off" />
          </Field>
          <Field label="Procedure code" htmlFor="procedureCode" error={fieldError('procedureCode')} hint="Required except for pharmacy claims">
            <input id="procedureCode" value={form.procedureCode} onChange={set('procedureCode')} maxLength={10} autoComplete="off" />
          </Field>
          <Field label="Amount claimed" htmlFor="amountClaimed" error={fieldError('amountClaimed')}>
            <input id="amountClaimed" inputMode="decimal" value={form.amountClaimed} onChange={set('amountClaimed')} autoComplete="off" />
          </Field>
          <div className="form-actions">
            <ErrorNotice error={error && Object.keys(error.fieldErrors).length === 0 ? error : undefined} />
            <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? 'Submitting…' : 'Submit claim'}</button>
          </div>
        </form>
      </Card>
    </>
  );
}
