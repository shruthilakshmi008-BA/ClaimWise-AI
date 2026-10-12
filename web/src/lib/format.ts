export const money = (n: number) => new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD' }).format(n);

export const dateOnly = (iso: string) => {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  return y && m && d ? new Date(Date.UTC(y, m - 1, d)).toLocaleDateString(undefined, { timeZone: 'UTC', year: 'numeric', month: 'short', day: 'numeric' }) : iso;
};

export const dateTime = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });

export const bytes = (n: number) => (n < 1024 ? `${n} B` : n < 1024 * 1024 ? `${(n / 1024).toFixed(1)} KB` : `${(n / 1024 / 1024).toFixed(1)} MB`);

/** "NeedsInformation" -> "Needs information" */
export const humanize = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase()).replace(/ ([A-Z])/g, (m) => m.toLowerCase());

export const percent = (n: number) => `${Math.round(n * 100)}%`;

/**
 * Splits a passage around a quoted excerpt so the cited words can be highlighted. The server verifies quotes after
 * ignoring case and punctuation, so matching here does the same. Returns null when the quote cannot be located.
 */
export function splitAroundQuote(text: string, quote: string): { before: string; match: string; after: string } | null {
  const words = quote.split(/[^\p{L}\p{N}]+/u).filter(Boolean);
  if (words.length === 0) return null;
  const escaped = words.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  const re = new RegExp(escaped.join('[^\\p{L}\\p{N}]+'), 'iu');
  const m = re.exec(text);
  if (!m) return null;
  return { before: text.slice(0, m.index), match: m[0], after: text.slice(m.index + m[0].length) };
}
