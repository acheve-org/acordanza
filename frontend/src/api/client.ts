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
      const body = await res.json().catch(() => ({}));
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
