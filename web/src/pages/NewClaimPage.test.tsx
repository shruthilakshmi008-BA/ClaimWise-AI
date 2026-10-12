import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { NewClaimPage, validate } from './NewClaimPage';
import { bodyOf, claimDetail, jsonResponse, mockApi, renderAt, requestsTo } from '../test/utils';

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Patient name'), 'Test Patient');
  await user.selectOptions(screen.getByLabelText('Claim type'), 'Outpatient');
  await user.type(screen.getByLabelText('Member ID'), 'MBR-900001');
  await user.type(screen.getByLabelText('Policy number'), 'POL-TEST-01');
  await user.type(screen.getByLabelText('Provider'), 'Demo Clinic');
  await user.type(screen.getByLabelText('Service date'), '2026-10-01');
  await user.type(screen.getByLabelText('Diagnosis code'), 'J06.9');
  await user.type(screen.getByLabelText('Procedure code'), 'PRC-101');
  await user.type(screen.getByLabelText('Amount claimed'), '1200.50');
}

describe('NewClaimPage', () => {
  it('blocks submission and explains each invalid field without calling the API', async () => {
    const fetchMock = mockApi({});
    const user = userEvent.setup();
    renderAt(<NewClaimPage />, '/claims/new');

    await user.click(screen.getByRole('button', { name: 'Submit claim' }));

    expect(await screen.findByText('Enter the patient name (at least 2 characters).')).toBeInTheDocument();
    expect(screen.getByText('Choose a claim type.')).toBeInTheDocument();
    expect(screen.getByText('Amount must be between 0.01 and 10,000,000.')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('submits trimmed, typed data and navigates to the new claim', async () => {
    const fetchMock = mockApi({ 'POST /api/claims': () => jsonResponse(claimDetail({ id: 42 }), 201) });
    const user = userEvent.setup();
    renderAt(<NewClaimPage />, '/claims/new', '/claims/new');
    await fillValid(user);

    await user.click(screen.getByRole('button', { name: 'Submit claim' }));

    await waitFor(() => expect(requestsTo(fetchMock, 'POST', '/api/claims')).toHaveLength(1));
    expect(bodyOf(requestsTo(fetchMock, 'POST', '/api/claims')[0]!)).toMatchObject({
      patientName: 'Test Patient', claimType: 'Outpatient', amountClaimed: 1200.5, serviceDate: '2026-10-01', procedureCode: 'PRC-101',
    });
    expect(await screen.findByText('elsewhere')).toBeInTheDocument(); // navigated away from the form
  });

  it('shows server-side field errors next to the fields', async () => {
    mockApi({ 'POST /api/claims': () => jsonResponse({ title: 'Validation', status: 400, errors: { MemberId: ['That member is not allowed.'] } }, 400) });
    const user = userEvent.setup();
    renderAt(<NewClaimPage />, '/claims/new');
    await fillValid(user);

    await user.click(screen.getByRole('button', { name: 'Submit claim' }));

    expect(await screen.findByText('That member is not allowed.')).toBeInTheDocument();
  });

  describe('validate', () => {
    const base = { patientName: 'Ab', memberId: 'MBR-100', policyNumber: 'POL-TEST', providerName: 'Clinic', claimType: 'Dental' as const, diagnosisCode: '', procedureCode: '', amountClaimed: '10', serviceDate: '2026-01-01' };

    it('accepts a valid form', () => expect(validate(base, new Date('2026-10-12'))).toEqual({}));
    it('rejects future dates, 3-decimal and out-of-range amounts, and malformed ids', () => {
      const e = validate({ ...base, serviceDate: '2026-12-01', amountClaimed: '1.005', memberId: 'x y', policyNumber: 'ab' }, new Date('2026-10-12'));
      expect(Object.keys(e).sort()).toEqual(['amountClaimed', 'memberId', 'policyNumber', 'serviceDate']);
      expect(validate({ ...base, amountClaimed: '0' }).amountClaimed).toBeDefined();
      expect(validate({ ...base, amountClaimed: '10000001' }).amountClaimed).toBeDefined();
    });
  });
});
