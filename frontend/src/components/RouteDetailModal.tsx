import { Fragment, useState, type ChangeEvent } from 'react';
import { Button, CompassRose, ConfirmModal, GradePill, Modal, RemoveButton, type Orientation } from 'acordanza-design-system';
import { ApiError } from '../api/client';
import type { CreateTickInput, RouteDetail } from '../api/types';
import { gearLabels, orientationLabels } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../hooks/useAuth';
import { formatDate } from '../format';
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
  /** Absent on pages that don't wire up enrichment yet — the button stays hidden either way. */
  onEnrich?: (force: boolean) => Promise<void>;
}

const ACCEPTED_TOPO_TYPES = 'image/jpeg,image/png,image/webp';

export function RouteDetailModal({
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
  onEnrich,
}: Props) {
  const { hasPermission } = useAuth();
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const [addingTick, setAddingTick] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [rejecting, setRejecting] = useState(false);
  const [enriching, setEnriching] = useState(false);
  const [enrichError, setEnrichError] = useState('');
  const [enrichStarted, setEnrichStarted] = useState(false);
  const [confirmingReEnrich, setConfirmingReEnrich] = useState(false);

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

  async function handleEnrich(force: boolean) {
    if (!onEnrich) return;
    setEnriching(true);
    setEnrichError('');
    try {
      await onEnrich(force);
      setEnrichStarted(true);
    } catch (err) {
      setEnrichError(
        err instanceof ApiError && (err.status === 409 || err.status === 503)
          ? err.message
          : 'No se pudo iniciar la búsqueda, inténtalo de nuevo',
      );
    } finally {
      setEnriching(false);
    }
  }

  return (
    <Fragment>
      <Modal onClose={onClose} wide={true}>
        <div className="detail-head">
          <div>
            <h2>{route.name}</h2>
            <p className="detail-meta">
              <GradePill grade={route.grade} obligatoryGrade={route.obligatoryGrade} />
              {route.areaPath}
            </p>
            {route.validationStatus === 'approved' && (
              <p className="detail-fa">
                ✓ Validada por {route.decidedByName} el {formatDate(route.decidedAt!)}
              </p>
            )}
            {route.validationStatus === 'rejected' && (
              <p className="detail-fa">
                ✗ Rechazada por {route.decidedByName} el {formatDate(route.decidedAt!)}
                {route.rejectionReason && ` · Motivo: ${route.rejectionReason}`}
              </p>
            )}
            {error && <p className="error-text">{error}</p>}
          </div>
          <div className="detail-actions">
            {route.validationStatus === 'pending' && route.canValidate && (
              <Button variant="ghost" small onClick={onValidate}>
                Validar
              </Button>
            )}
            {route.validationStatus === 'pending' && route.canValidate && (
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
        <div className="detail-section">
          <div className="detail-stats">
            <div className="detail-stat">
              <span className="detail-stat-label">Grado</span>
              <span className="detail-stat-value">{route.grade}</span>
            </div>
            {route.length > 0 && (
              <div className="detail-stat">
                <span className="detail-stat-label">Longitud</span>
                <span className="detail-stat-value">{route.length} m</span>
              </div>
            )}
            {route.orientation && (
              <div className="detail-stat">
                <span className="detail-stat-label">Orientación</span>
                <span className="detail-stat-value">
                  <CompassRose size="md" value={route.orientation as Orientation} />
                  {orientationLabels[route.orientation as Orientation]}
                </span>
              </div>
            )}
            {route.gear && (
              <div className="detail-stat">
                <span className="detail-stat-label">Material</span>
                <span className="detail-stat-value">{gearLabels[route.gear]}</span>
              </div>
            )}
          </div>
          {route.obligatoryGrade && (
            <p className="detail-fa">Grado obligatorio: {route.obligatoryGrade}</p>
          )}
        </div>

        <div className="detail-section">
          <p className="detail-desc">{route.description || 'Sin descripción todavía.'}</p>
        </div>

        {(route.recommendedGear || route.optionalGear) && (
          <div className="detail-section">
            {route.recommendedGear && <p className="detail-fa">Material recomendado: {route.recommendedGear}</p>}
            {route.optionalGear && <p className="detail-fa">Material opcional: {route.optionalGear}</p>}
          </div>
        )}

        {route.links.length > 0 && (
          <div className="detail-section">
            <h3>Enlaces</h3>
            <div className="tag-row">
              {route.links.map((link) => (
                <a key={link.url} href={link.url} target="_blank" rel="noreferrer" className="detail-link">
                  {link.url}
                  {/* Enrichment links are appended by the worker without a human ever
                      looking at them (plan 29 §6) — the badge is how a reader knows which
                      ones to distrust, and which ones to delete from the edit form. An
                      import link is a weaker claim: a person chose that page and pasted
                      it, they just didn't type the route in themselves (plan 31 §5). */}
                  {link.source === 'enrichment' && <span className="detail-link-source">automático</span>}
                  {link.source === 'import' && <span className="detail-link-source">importado</span>}
                </a>
              ))}
            </div>
          </div>
        )}

        {route.firstAscensionists.length > 0 && (
          <div className="detail-section">
            <p className="detail-fa">
              Apertura:{' '}
              {route.firstAscensionists
                .map((o) => (o.linkedUserName ? `${o.name} (${o.linkedUserName})` : o.name))
                .join(', ')}
            </p>
          </div>
        )}

        <div className="detail-section">
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
        </div>

        {onEnrich && hasPermission(Permissions.routeEnrich) && (
          <div className="detail-section">
            <h3>Búsqueda automática</h3>
            <Button
              variant="ghost"
              small
              disabled={enriching}
              onClick={() => (route.enrichedAt ? setConfirmingReEnrich(true) : handleEnrich(false))}
            >
              {route.enrichedAt ? 'Volver a buscar' : 'Buscar enlaces y topo'}
            </Button>
            {route.enrichedAt && (
              <p className="detail-fa">Última búsqueda: {formatDate(route.enrichedAt)}</p>
            )}
            {enrichStarted && (
              <p className="detail-fa">
                Buscando en la web. Los resultados aparecerán en unos minutos; recarga la vía para verlos.
              </p>
            )}
            {enrichError && <p className="error-text">{enrichError}</p>}
          </div>
        )}

        {hasPermission(Permissions.tickCreate) && (
          <div className="detail-section">
            <Button variant="ghost" small onClick={() => setAddingTick(true)}>
              {route.hasTick ? 'Registrar otro ascenso' : '+ Registrar ascenso'}
            </Button>
          </div>
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

      {confirmingReEnrich && (
        <ConfirmModal
          message="¿Volver a buscar enlaces y topo para esta vía? Se sustituirá la búsqueda anterior."
          confirmLabel="Volver a buscar"
          onConfirm={async () => {
            await handleEnrich(true);
            setConfirmingReEnrich(false);
          }}
          onClose={() => setConfirmingReEnrich(false)}
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
