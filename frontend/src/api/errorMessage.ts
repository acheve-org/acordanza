import { ApiError } from './client';

// Shared shape for presenting query/mutation errors: ApiError.message already
// carries the backend's Spanish `{ error }` text, so surface it directly and
// fall back to a generic Spanish message otherwise.
export function errorMessage(err: unknown, fallback: string): string {
  return err instanceof ApiError ? err.message : fallback;
}
