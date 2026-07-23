import { api } from './client';
import type { PasskeySummary } from './types';

export function listPasskeys(): Promise<PasskeySummary[]> {
  return api.get<PasskeySummary[]>('/passkeys');
}

export function getRegisterOptions(name: string): Promise<{ sessionId: string; options: unknown }> {
  return api.post<{ sessionId: string; options: unknown }>('/passkeys/register/options', { name });
}

export function completeRegister(sessionId: string, attestationResponse: unknown): Promise<PasskeySummary> {
  return api.post<PasskeySummary>('/passkeys/register/complete', { sessionId, attestationResponse });
}

export function deletePasskey(id: number): Promise<{ ok: true }> {
  return api.delete<{ ok: true }>(`/passkeys/${id}`);
}
