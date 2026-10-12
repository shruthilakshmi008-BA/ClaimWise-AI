import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

type Ctx = [string, (v: string) => void];
const ReviewerContext = createContext<Ctx | null>(null);

const KEY = 'claimwise.reviewer';

/**
 * The reviewer name is self-declared (there is no login yet). It is shared by every panel and remembered in this
 * browser for convenience only; the server records whatever name is submitted with each action.
 */
export function ReviewerProvider({ children }: { children: ReactNode }) {
  const [name, setName] = useState(() => {
    try { return localStorage.getItem(KEY) ?? ''; } catch { return ''; }
  });
  const update = useCallback((v: string) => {
    setName(v);
    try { localStorage.setItem(KEY, v); } catch { /* storage unavailable: fine */ }
  }, []);
  const value = useMemo<Ctx>(() => [name, update], [name, update]);
  return <ReviewerContext.Provider value={value}>{children}</ReviewerContext.Provider>;
}

export function useReviewerName(): Ctx {
  const ctx = useContext(ReviewerContext);
  const local = useState('');
  return ctx ?? local;
}
