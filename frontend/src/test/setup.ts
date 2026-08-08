import '@testing-library/jest-dom/vitest';
import { server } from './server';

// 'error' (not the default 'warn') so a request the test forgot to mock — or a component
// prefetching data it shouldn't (see frontend/CLAUDE.md's "modals fetch their own data"
// rule) — fails the test instead of silently falling through to a real network call.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  server.resetHandlers();
  // AuthContext persists a session here; without this, a stored token from one test's
  // real-AuthProvider login leaks into the next test's bootstrap.
  localStorage.clear();
});
afterAll(() => server.close());
