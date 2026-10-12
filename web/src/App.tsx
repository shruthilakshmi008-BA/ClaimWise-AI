import { NavLink, Route, Routes } from 'react-router-dom';
import { ClaimDetailPage } from './pages/ClaimDetailPage';
import { ClaimsPage } from './pages/ClaimsPage';
import { DashboardPage } from './pages/DashboardPage';
import { NewClaimPage } from './pages/NewClaimPage';
import { PoliciesPage } from './pages/PoliciesPage';

const nav = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/claims', label: 'Claims', end: false },
  { to: '/claims/new', label: 'New claim', end: true },
  { to: '/policies', label: 'Policies', end: false },
];

export function App() {
  return (
    <>
      <a className="skip-link" href="#main">Skip to content</a>
      <header className="app-header">
        <div className="app-header-inner">
          <div className="brand">
            <span className="brand-mark" aria-hidden="true">✓</span>
            <span>ClaimWise <strong>AI</strong></span>
          </div>
          <nav aria-label="Main">
            {nav.map((n) => (
              <NavLink key={n.to} to={n.to} end={n.end}>{n.label}</NavLink>
            ))}
          </nav>
        </div>
        <p className="banner" role="note">
          Synthetic demo data only — do not enter real patient or insurance information. AI output is an aid for a human reviewer, never a decision.
        </p>
      </header>
      <main id="main" className="container">
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/claims" element={<ClaimsPage />} />
          <Route path="/claims/new" element={<NewClaimPage />} />
          <Route path="/claims/:id" element={<ClaimDetailPage />} />
          <Route path="/policies" element={<PoliciesPage />} />
          <Route path="*" element={<p>Page not found.</p>} />
        </Routes>
      </main>
    </>
  );
}
