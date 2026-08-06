import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import './styles.css';
import App from './App.tsx';
import { AuthProvider } from './context/AuthContext.tsx';
import { PendingTasksProvider } from './context/PendingTasksContext.tsx';
import { LoadingBar } from './components/LoadingBar.tsx';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LoadingBar />
    <BrowserRouter>
      <AuthProvider>
        <PendingTasksProvider>
          <App />
        </PendingTasksProvider>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
);
