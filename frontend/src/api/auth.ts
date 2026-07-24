import { api } from './client';

export function forgotPassword(email: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/auth/forgot-password', { email });
}

export function resetPassword(email: string, token: string, newPassword: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/auth/reset-password', { email, token, newPassword });
}
