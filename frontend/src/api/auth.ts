import { api } from './client';

export function forgotPassword(email: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/auth/forgot-password', { email });
}

export function resetPassword(email: string, token: string, newPassword: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/auth/reset-password', { email, token, newPassword });
}

export function acceptInvite(email: string, token: string, name: string, password: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/auth/accept-invite', { email, token, name, password });
}

export function validateToken(email: string, token: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/auth/validate-token', { email, token });
}
