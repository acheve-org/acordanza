import { setupServer } from 'msw/node';

// No default handlers: every test registers exactly the routes it needs via
// `server.use(...)`, so an unhandled request (see setup.ts's `onUnhandledRequest: 'error'`)
// means a real mismatch between what the code fetched and what the test expected.
export const server = setupServer();
