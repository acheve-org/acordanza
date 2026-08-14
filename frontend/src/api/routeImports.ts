import { api } from './client';
import type { RouteImport } from './types';

// One page, two kinds, two sub-paths (see backend RouteImportsController for why the two
// kinds don't share one endpoint despite sharing everything else).

// Multipart, like uploadTopo — postForm skips the JSON Content-Type so the browser sets
// its own boundary. The sector is part of the form rather than inferred from the photo:
// see backend/Acordanza.Core/Models/RouteImport.cs.
export function createImageImport(file: File, areaId: number): Promise<RouteImport> {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('areaId', String(areaId));
  return api.postForm<RouteImport>('/route-imports/image', formData);
}

export function createUrlImport(url: string, areaId: number): Promise<RouteImport> {
  return api.post<RouteImport>('/route-imports/url', { url, areaId });
}

export function listMyRouteImports(): Promise<RouteImport[]> {
  return api.get<RouteImport[]>('/route-imports/mine');
}
