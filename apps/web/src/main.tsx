import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import { router } from './routes';
import { AuthProvider } from './features/auth/AuthProvider';
import './index.css';

const container = document.getElementById('root');

if (!container) {
  throw new Error('Root element is missing from index.html.');
}

createRoot(container).render(
  <StrictMode>
    <AuthProvider><RouterProvider router={router} /></AuthProvider>
  </StrictMode>,
);
