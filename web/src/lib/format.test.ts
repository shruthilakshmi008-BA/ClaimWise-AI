import { describe, expect, it } from 'vitest';
import { dateOnly, humanize, splitAroundQuote } from './format';

describe('splitAroundQuote', () => {
  const text = '2.1 Annual outpatient limit. The plan pays outpatient expenses up to 5,000 per member per policy year.';

  it('finds the quote ignoring case, spacing and punctuation differences', () => {
    const parts = splitAroundQuote(text, 'the PLAN pays   outpatient expenses up to 5,000');
    expect(parts?.match).toBe('The plan pays outpatient expenses up to 5,000');
    expect(parts?.before).toBe('2.1 Annual outpatient limit. ');
    expect(parts?.after).toBe(' per member per policy year.');
  });

  it('returns null rather than guessing when the quote is not in the passage', () => {
    expect(splitAroundQuote(text, 'unlimited cover for everyone')).toBeNull();
    expect(splitAroundQuote(text, '   ')).toBeNull();
  });

  it('is safe with regex metacharacters in the quote', () => {
    // Punctuation at the edges is ignored (as on the server), but nothing in the quote may break the pattern.
    expect(() => splitAroundQuote('Pay (5,000) [max] now', '(5,000) [max] .*+?^${}|\\')).not.toThrow();
    expect(splitAroundQuote('Pay (5,000) [max] now', '(5,000) [max]')?.match).toBe('5,000) [max');
  });
});

describe('formatting', () => {
  it('humanizes enum names', () => {
    expect(humanize('NeedsInformation')).toBe('Needs information');
    expect(humanize('InReview')).toBe('In review');
  });

  it('formats date-only values without shifting the day across time zones', () => {
    expect(dateOnly('2026-10-07')).toMatch(/7/);
  });
});
