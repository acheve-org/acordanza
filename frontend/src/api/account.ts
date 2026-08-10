import { api } from './client';

export function changePassword(currentPassword: string, newPassword: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/account/change-password', { currentPassword, newPassword });
}

export function updateProfile(name: string): Promise<{ name: string }> {
  return api.put<{ name: string }>('/account/profile', { name });
}

export function requestEmailChange(newEmail: string, currentPassword: string): Promise<{ ok: true }> {
  return api.post<{ ok: true }>('/account/email/change-request', { newEmail, currentPassword });
}
