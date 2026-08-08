// client.ts falls back to this when VITE_API_URL isn't set, which is the case in the test
// environment (no .env loaded) — MSW handlers match against it.
export const API_URL = 'http://localhost:5073/api';
