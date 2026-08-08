import { api } from './client';
import type { MyContributions } from './types';

export function getMyContributions(): Promise<MyContributions> {
  return api.get<MyContributions>('/contributions/mine');
}
