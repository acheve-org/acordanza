import { Fragment, useState, type ChangeEvent } from 'react';
import { Button, ConfirmModal, GradePill, Modal, RemoveButton } from 'acordanza-design-system';
import type { CreateTickInput, RouteDetail } from '../api/types';
import { gearLabels, orientationLabels } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';
import { AddTickModal } from './AddTickModal';
import { RejectReasonModal } from './RejectReasonModal';

interface Props {
  route: RouteDetail | null;
  /** Error from a delete/validate/reject action, surfaced by the caller (e.g. a mutation's onError). */
  error?: string;
  onClose: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onValidate: () => Promise<void>;
  onReject: (reason: string) => Promise<unknown>;
  onAddTick: (input: CreateTickInput) => Promise<void>;
  onUploadTopo: (file: File) => Promise<void>;
  onDeleteTopo: (topoId: number) => Promise<void>;
}

const ACCEPTED_TOPO_TYPES = 'image/jpeg,image/png,image/webp';

export function DetailModal({
  route,
  error,
  onClose,
  onEdit,
  onDelete,
  onValidate,
  onReject,
  onAddTick,
  onUploadTopo,
  onDeleteTopo,
}: Props) {
  const { hasPermission } = useAuth();
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const [addingTick, setAddingTick] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [rejecting, setRejecting] = useState(false);

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

  return (
    <Fragment>
      <Modal onClose={onClose}>
        <div className="detail-head">
          <div>
            <h2>{route.name}</h2>
            <p className="detail-meta">
              <GradePill>{route.grade}</GradePill>
              {route.areaPath}
            </p>
            <p className="detail-fa">
              {[route.orientation && orientationLabels[route.orientation], route.gear && gearLabels[route.gear]]
                .filter(Boolean)
                .join(' · ')}
            </p>
            {route.validationStatus === 'approved' && (
              <p className="detail-fa">
                ✓ Validada por {route.decidedByName} el {new Date(route.decidedAt!).toLocaleDateString()}
              </p>
            )}
            {route.validationStatus === 'rejected' && (
              <p className="detail-fa">
                ✗ Rechazada por {route.decidedByName} el {new Date(route.decidedAt!).toLocaleDateString()}
                {route.rejectionReason && ` · Motivo: ${route.rejectionReason}`}
              </p>
            )}
            {error && <p className="error-text">{error}</p>}
          </div>
          <div className="detail-actions">
            {route.validationStatus !== 'approved' && route.canValidate && (
              <Button variant="ghost" small onClick={onValidate}>
                Validar
              </Button>
            )}
            {route.validationStatus !== 'rejected' && route.canValidate && (
              <Button variant="ghost" small danger onClick={() => setRejecting(true)}>
                Rechazar
              </Button>
            )}
            {hasPermission(Permissions.routeEdit) && (
              <Button variant="ghost" small onClick={onEdit}>
                Editar
              </Button>
            )}
            {hasPermission(Permissions.routeDelete) && (
              <Button variant="ghost" small danger onClick={() => setConfirmingDelete(true)}>
                Eliminar
              </Button>
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

        <hr className="divider" />

        <h3>Topos</h3>
        {route.topos.length > 0 && (
          <div className="topo-grid">
            {route.topos.map((topo) => (
              <div key={topo.id} className="topo-thumb">
                <a href={topo.url} target="_blank" rel="noreferrer">
                  <img src={topo.url} alt={topo.fileName} loading="lazy" />
                </a>
                <RemoveButton variant="overlay" label="Eliminar" onClick={() => onDeleteTopo(topo.id)} />
              </div>
            ))}
          </div>
        )}
        <Button as="label" variant="ghost" small className="topo-upload">
          {uploading ? 'Subiendo…' : '+ Añadir topo'}
          <input
            type="file"
            accept={ACCEPTED_TOPO_TYPES}
            multiple
            disabled={uploading}
            onChange={handleTopoFilesSelected}
            className="hidden"
          />
        </Button>
        {uploadError && <p className="error-text">{uploadError}</p>}

        <hr className="divider" />

        {hasPermission(Permissions.tickCreate) && (
          <Button variant="ghost" small onClick={() => setAddingTick(true)}>
            {route.hasTick ? 'Registrar otro ascenso' : '+ Registrar ascenso'}
          </Button>
        )}
      </Modal>

      {addingTick && (
        <AddTickModal routeName={route.name} onClose={() => setAddingTick(false)} onSubmit={onAddTick} />
      )}

      {confirmingDelete && (
        <ConfirmModal
          message="¿Eliminar esta vía y todos sus ascensos registrados?"
          confirmLabel="Eliminar"
          danger
          onConfirm={onDelete}
          onClose={() => setConfirmingDelete(false)}
        />
      )}

      {rejecting && (
        <RejectReasonModal
          title={`Rechazar "${route.name}"`}
          onClose={() => setRejecting(false)}
          onReject={async (reason) => {
            await onReject(reason);
            setRejecting(false);
          }}
        />
      )}
    </Fragment>
  );
}
