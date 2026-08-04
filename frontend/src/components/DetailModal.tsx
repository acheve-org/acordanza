import { Fragment, useState, type ChangeEvent } from 'react';
import type { CreateTickInput, RouteDetail } from '../api/types';
import { gearLabels, orientationLabels } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';
import { AddTickModal } from './AddTickModal';

interface Props {
  route: RouteDetail | null;
  onClose: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onValidate: () => Promise<void>;
  onAddTick: (input: CreateTickInput) => Promise<void>;
  onUploadTopo: (file: File) => Promise<void>;
  onDeleteTopo: (topoId: number) => Promise<void>;
}

const ACCEPTED_TOPO_TYPES = 'image/jpeg,image/png,image/webp';

export function DetailModal({ route, onClose, onEdit, onDelete, onValidate, onAddTick, onUploadTopo, onDeleteTopo }: Props) {
  const { hasPermission } = useAuth();
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const [addingTick, setAddingTick] = useState(false);

  if (!route) return null;

  async function handleTopoFilesSelected(e: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';
    if (files.length === 0) return;

    setUploading(true);
    setUploadError('');
    try {
      for (const file of files) {
        await onUploadTopo(file);
      }
    } catch {
      setUploadError('No se pudo subir la imagen');
    } finally {
      setUploading(false);
    }
  }

  const tags = route.tags
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean);

  return (
    <Fragment>
      <div
        className="modal-overlay"
        onClick={(e) => {
          if (e.target === e.currentTarget) onClose();
        }}
      >
        <div className="modal">
          <div className="detail-head">
            <div>
              <h2>{route.name}</h2>
              <p className="detail-meta">
                {route.grade} · {route.areaPath}
              </p>
              <p className="detail-fa">
                {[route.orientation && orientationLabels[route.orientation], route.gear && gearLabels[route.gear]]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
              {route.validatedAt && (
                <p className="detail-fa">
                  ✓ Validada por {route.validatedBy} el {new Date(route.validatedAt).toLocaleDateString()}
                </p>
              )}
            </div>
            <div className="detail-actions">
              {!route.validatedAt && route.canValidate && (
                <button className="ghost-btn small" onClick={onValidate}>
                  Validar
                </button>
              )}
              {hasPermission(Permissions.routeEdit) && (
                <button className="ghost-btn small" onClick={onEdit}>
                  Editar
                </button>
              )}
              {hasPermission(Permissions.routeDelete) && (
                <button className="ghost-btn small danger" onClick={onDelete}>
                  Eliminar
                </button>
              )}
            </div>
          </div>
          <p className="detail-desc">{route.description || 'Sin descripción todavía.'}</p>
          {route.firstAscensionists.length > 0 && (
            <p className="detail-fa">
              Apertura:{' '}
              {route.firstAscensionists
                .map((o) => (o.linkedUserName ? `${o.name} (${o.linkedUserName})` : o.name))
                .join(', ')}
            </p>
          )}
          <div className="tag-row">
            {tags.map((t) => (
              <span key={t} className="tag-chip">
                {t}
              </span>
            ))}
          </div>

          <hr className="divider" />

          <h3>Topos</h3>
          {route.topos.length > 0 && (
            <div className="topo-grid">
              {route.topos.map((topo) => (
                <div key={topo.id} className="topo-thumb">
                  <a href={topo.url} target="_blank" rel="noreferrer">
                    <img src={topo.url} alt={topo.fileName} loading="lazy" />
                  </a>
                  <button className="topo-remove" title="Eliminar" onClick={() => onDeleteTopo(topo.id)}>
                    &times;
                  </button>
                </div>
              ))}
            </div>
          )}
          <label className="topo-upload ghost-btn small">
            {uploading ? 'Subiendo…' : '+ Añadir topo'}
            <input
              type="file"
              accept={ACCEPTED_TOPO_TYPES}
              multiple
              disabled={uploading}
              onChange={handleTopoFilesSelected}
              className="hidden"
            />
          </label>
          {uploadError && <p className="error-text">{uploadError}</p>}

          <hr className="divider" />

          {hasPermission(Permissions.tickCreate) && (
            <button className="ghost-btn small" onClick={() => setAddingTick(true)}>
              {route.hasTick ? 'Registrar otro ascenso' : '+ Registrar ascenso'}
            </button>
          )}
        </div>
      </div>

      {addingTick && (
        <AddTickModal routeName={route.name} onClose={() => setAddingTick(false)} onSubmit={onAddTick} />
      )}
    </Fragment>
  );
}
