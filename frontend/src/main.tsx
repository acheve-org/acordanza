import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import './styles.css';
import App from './App.tsx';
import { AuthProvider } from './context/AuthContext.tsx';
import { PendingTasksProvider } from './context/PendingTasksContext.tsx';
import { LoadingBar } from './components/LoadingBar.tsx';
import { queryClient } from './queryClient.ts';
import * as analytics from './lib/analytics.ts';

analytics.init();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LoadingBar />
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <PendingTasksProvider>
            <App />
          </PendingTasksProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
