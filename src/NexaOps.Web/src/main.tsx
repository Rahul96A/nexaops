import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ApiError } from '@/api/client';
import { AuthProvider } from '@/auth/AuthProvider';
import { App } from './App';

/**
 * Query defaults.
 *
 * The retry policy is the part worth reading: retrying a 401, 403 or 404 is pointless and,
 * for a 429, actively harmful - it turns one rate-limited request into four. Only genuinely
 * transient failures are retried.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        if (error instanceof ApiError) {
          if (error.status < 500 || error.status === 429) {
            return false;
          }
        }

        return failureCount < 2;
      },
    },
    mutations: {
      // A failed write is never retried automatically: the user should decide, because a
      // silent replay of a create is how duplicate records happen.
      retry: false,
    },
  },
});

const container = document.getElementById('root');

if (!container) {
  throw new Error('The root element is missing from index.html.');
}

createRoot(container).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
