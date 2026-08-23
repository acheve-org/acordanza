import type { ApiErrorBody } from './types';

const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5073/api';

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

let authToken: string | null = null;

export function setAuthToken(token: string | null) {
  authToken = token;
}

// Invoked when the API rejects a request that *did* carry a token. AuthProvider registers
// the sign-out here; nothing else may, and it stays a callback rather than an import so
// this module keeps no dependency on React or on the auth context.
let onUnauthorized: (() => void) | null = null;

export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler;
}

// Tracks in-flight requests so a global loading indicator (see LoadingBar)
// can react without every call site managing its own loading state.
let activeRequestCount = 0;
const activityListeners = new Set<() => void>();

export function subscribeToApiActivity(listener: () => void): () => void {
  activityListeners.add(listener);
  return () => activityListeners.delete(listener);
}

export function getApiActivitySnapshot(): boolean {
  return activeRequestCount > 0;
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  // FormData bodies must not get an explicit Content-Type — the browser
  // sets one itself (multipart/form-data with the correct boundary).
  const isFormData = options.body instanceof FormData;
  const headers: Record<string, string> = {
    ...(options.body && !isFormData ? { 'Content-Type': 'application/json' } : {}),
    ...(authToken ? { Authorization: `Bearer ${authToken}` } : {}),
  };

  activeRequestCount++;
  activityListeners.forEach((listener) => listener());
  try {
    const res = await fetch(`${API_URL}${path}`, { ...options, headers });

    if (!res.ok) {
      const body: Partial<ApiErrorBody> = await res.json().catch(() => ({}));

      // A 401 on a request that carried a token means the session is over, not that this
      // one call failed. Nothing refreshes the JWT, so every later request fails the same
      // way — without this the user sits on a page of "Error en la solicitud (401)" panels
      // with no way to read what happened, still apparently logged in. Handing it to
      // AuthProvider turns it into a return to the login screen with an explanation.
      //
      // Guarded on `authToken` because a wrong password is also a 401: that one arrives
      // with no token attached, and must stay an ordinary form error. The handler clears
      // the token synchronously, so when a page's five queries all 401 together the first
      // one signs out and the rest fall through to the plain error below.
      if (res.status === 401 && authToken) {
        onUnauthorized?.();
        throw new ApiError(res.status, 'Tu sesión ha caducado, vuelve a iniciar sesión');
      }

      throw new ApiError(res.status, body.error ?? `Error en la solicitud (${res.status})`);
    }

    if (res.status === 204) return undefined as T;
    return (await res.json()) as T;
  } finally {
    activeRequestCount--;
    activityListeners.forEach((listener) => listener());
  }
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body !== undefined ? JSON.stringify(body) : undefined }),
  postForm: <T>(path: string, formData: FormData) => request<T>(path, { method: 'POST', body: formData }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body !== undefined ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
};
