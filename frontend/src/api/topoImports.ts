import { api } from './client';
import type { TopoImport } from './types';

// Multipart, like uploadTopo — postForm skips the JSON Content-Type so the browser
// sets its own boundary. The sector is part of the form rather than inferred from the
// photo: see backend/Acordanza.Core/Models/TopoImport.cs.
export function createTopoImport(file: File, areaId: number): Promise<TopoImport> {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('areaId', String(areaId));
  return api.postForm<TopoImport>('/topo-imports', formData);
}

export function listMyTopoImports(): Promise<TopoImport[]> {
  return api.get<TopoImport[]>('/topo-imports/mine');
}
