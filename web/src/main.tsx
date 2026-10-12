import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './App';
import { ReviewerProvider } from './lib/reviewer';
import './styles.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <ReviewerProvider>
        <App />
      </ReviewerProvider>
    </BrowserRouter>
  </StrictMode>,
);
