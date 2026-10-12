import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { AiSummaryPanel, SummaryCard } from './AiSummaryPanel';
import { answeredSummary, bodyOf, jsonResponse, mockApi, policyDoc, renderAt, requestsTo } from '../../test/utils';

describe('SummaryCard', () => {
  it('shows findings with citation chips that reveal the verbatim source with the quote highlighted', async () => {
    mockApi({});
    const user = userEvent.setup();
    renderAt(<SummaryCard claimId={1} summary={answeredSummary()} onChanged={() => {}} />);

    expect(screen.getByText(/AI-assisted — verify every citation/)).toBeInTheDocument();
    expect(screen.getByText('Extractive quoting (no language model)', { exact: false })).toBeInTheDocument();
    expect(screen.queryByLabelText('Source P1')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: '[P1]' }));

    const source = screen.getByLabelText('Source P1');
    expect(within(source).getByText(/Gold Plan Policy · clause 2.1 · page 1/)).toBeInTheDocument();
    expect(source.querySelector('mark')).toHaveTextContent('The plan pays outpatient expenses up to 5,000');
  });

  it('labels language-model output with the model name', () => {
    mockApi({});
    renderAt(<SummaryCard claimId={1} summary={answeredSummary({ usedLanguageModel: true, model: 'claude-opus-5-5', generator: 'llm:anthropic' })} onChanged={() => {}} />);
    expect(screen.getByText(/Language model: claude-opus-5-5/)).toBeInTheDocument();
  });

  it('shows an abstention as "no findings", with the reason and the missing terms', () => {
    mockApi({});
    renderAt(<SummaryCard claimId={1} onChanged={() => {}} summary={answeredSummary({
      status: 'Abstained', statements: [], review: { state: 'NotRequired', reviewedBy: null, reviewedAt: null, note: null },
      abstainReason: 'Insufficient policy evidence: The supporting passages cover only 12% of the question’s key terms.',
      evidence: { verdict: 'Insufficient', reasons: ['low coverage'], topScore: 0.1, coverage: 0.12, supportingPassages: 0, missingTerms: ['helicopter', 'cryotherapy'] },
    })} />);

    expect(screen.getByText('Insufficient evidence — no findings shown.')).toBeInTheDocument();
    expect(screen.getByText(/cover only 12%/)).toBeInTheDocument();
    expect(screen.getByText(/helicopter, cryotherapy/)).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: /statements/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Accept summary' })).not.toBeInTheDocument(); // nothing to review
  });

  it('shows a provider failure without presenting any content as a finding', () => {
    mockApi({});
    renderAt(<SummaryCard claimId={1} onChanged={() => {}} summary={answeredSummary({
      status: 'Failed', statements: [], failureReason: 'The language model provider did not answer within 60s.',
      review: { state: 'NotRequired', reviewedBy: null, reviewedAt: null, note: null },
    })} />);

    expect(screen.getByRole('alert')).toHaveTextContent('did not answer within 60s');
    expect(screen.getByRole('alert')).toHaveTextContent('No findings were generated');
  });

  it('surfaces conflicting policy wording and system notices prominently', () => {
    mockApi({});
    renderAt(<SummaryCard claimId={1} onChanged={() => {}} summary={answeredSummary({
      conflicts: [{ kind: 'ValueConflict', passages: ['P1', 'P2'], description: 'P1 states 5,000 while P2 states 6,000.' }],
      statements: [{ ordinal: 1, origin: 'System', text: 'Conflicting policy wording: P1 states 5,000 while P2 states 6,000.', citations: [{ passage: 'P1', quote: 'up to 5,000' }] }],
    })} />);

    const alert = screen.getByText('Conflicting policy wording', { selector: 'strong' }).closest('[role=alert]')!;
    expect(alert).toHaveTextContent('does not choose a side');
    expect(screen.getByText('System notice')).toBeInTheDocument();
  });

  it('lists statements that were removed because their citations failed verification', () => {
    mockApi({});
    renderAt(<SummaryCard claimId={1} onChanged={() => {}} summary={answeredSummary({
      rejectedStatements: [{ text: 'Cover is unlimited.', reason: 'Quote for P1 does not appear verbatim in that passage.' }],
    })} />);
    expect(screen.getByText(/1 generated statement\(s\) removed/)).toBeInTheDocument();
  });

  it('requires a note to reject, then records the rejection under the reviewer’s name', async () => {
    const onChanged = vi.fn();
    const fetchMock = mockApi({ 'POST /api/claims/1/summaries/7/review': () => jsonResponse(answeredSummary()) });
    const user = userEvent.setup();
    renderAt(<SummaryCard claimId={1} summary={answeredSummary()} onChanged={onChanged} />);
    const box = screen.getByRole('group', { name: 'Review summary 7' });

    await user.click(within(box).getByRole('button', { name: 'Reject summary' }));
    expect(await within(box).findByText('Enter your name to record the review.')).toBeInTheDocument();

    await user.type(within(box).getByLabelText('Reviewer name'), 'Rita Reviewer');
    await user.click(within(box).getByRole('button', { name: 'Reject summary' }));
    expect(await within(box).findByText('A note is required when rejecting an AI summary.')).toBeInTheDocument();
    expect(requestsTo(fetchMock, 'POST', '/api/claims/1/summaries/7/review')).toHaveLength(0);

    await user.type(within(box).getByLabelText('Note (required to reject)'), 'Wrong plan.');
    await user.click(within(box).getByRole('button', { name: 'Reject summary' }));

    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    expect(bodyOf(requestsTo(fetchMock, 'POST', '/api/claims/1/summaries/7/review')[0]!)).toEqual({ decision: 'Rejected', reviewerName: 'Rita Reviewer', note: 'Wrong plan.' });
  });

  it('shows who reviewed a summary once it has been reviewed', () => {
    mockApi({});
    renderAt(<SummaryCard claimId={1} onChanged={() => {}} summary={answeredSummary({
      review: { state: 'Accepted', reviewedBy: 'Rita Reviewer', reviewedAt: '2026-10-10T10:00:00Z', note: 'Checked clause 2.1.' },
    })} />);
    expect(screen.getByText('Rita Reviewer').closest('p')).toHaveTextContent(/Accepted by Rita Reviewer/);
    expect(screen.getByText(/Checked clause 2.1/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Accept summary' })).not.toBeInTheDocument();
  });
});

describe('AiSummaryPanel', () => {
  it('asks for the reviewer name before generating, then requests evidence for the chosen documents', async () => {
    const onChanged = vi.fn();
    const fetchMock = mockApi({
      'GET /api/policies': [policyDoc(), policyDoc({ id: 2, title: 'Basic Plan Policy', policyFamily: 'basic-plan' })],
      'POST /api/claims/1/summaries': () => jsonResponse(answeredSummary(), 201),
    });
    const user = userEvent.setup();
    localStorage.setItem('claimwise.reviewer', '');
    renderAt(<AiSummaryPanel claimId={1} isFinal={false} summaries={[]} onChanged={onChanged} />);

    const form = await screen.findByRole('form', { name: 'Generate AI summary' });
    await user.click(within(form).getByRole('button', { name: 'Find policy evidence' }));
    expect(await screen.findByText(/Enter your name/)).toBeInTheDocument();
    expect(requestsTo(fetchMock, 'POST', '/api/claims/1/summaries')).toHaveLength(0);
  });

  it('sends the question, requester and document scope', async () => {
    localStorage.setItem('claimwise.reviewer', 'Rita Reviewer');
    const onChanged = vi.fn();
    const fetchMock = mockApi({
      'GET /api/policies': [policyDoc(), policyDoc({ id: 2, title: 'Basic Plan Policy', policyFamily: 'basic-plan' })],
      'POST /api/claims/1/summaries': () => jsonResponse(answeredSummary(), 201),
    });
    const user = userEvent.setup();
    renderAt(<AiSummaryPanel claimId={1} isFinal={false} summaries={[]} onChanged={onChanged} />);
    const form = await screen.findByRole('form', { name: 'Generate AI summary' });

    await user.type(within(form).getByLabelText(/Question/), 'dental waiting period');
    await user.click(await within(form).findByLabelText(/Basic Plan Policy/));
    await user.click(within(form).getByRole('button', { name: 'Find policy evidence' }));

    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    expect(bodyOf(requestsTo(fetchMock, 'POST', '/api/claims/1/summaries')[0]!)).toEqual({
      question: 'dental waiting period', requestedBy: 'Rita Reviewer', documentIds: [2],
    });
  });
});
