import { Fragment, useState, type ChangeEvent } from 'react';
import {
  Button,
  CompassRose,
  ConfirmModal,
  GradePill,
  Icon,
  Modal,
  orientationAbbr,
  RemoveButton,
  type Orientation,
} from 'acordanza-design-system';
import { ApiError } from '../api/client';
import type { CreateTickInput, RouteDetail } from '../api/types';
import { gearLabels } from '../api/types';
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

  const canReview = route.validationStatus === 'pending' && route.canValidate;
  const canManage = hasPermission(Permissions.routeEdit) || hasPermission(Permissions.routeDelete);

  return (
    <Fragment>
      <Modal onClose={onClose} wide={true}>
        {/* The head is identity only — every action lives in the rail below, so nothing
            here competes with the route's name for the first look. */}
        <div className="detail-head">
          <div className="detail-head-row">
            <h2>{route.name}</h2>
            <span className="detail-head-facts">
              {route.gear && <span>{gearLabels[route.gear]}</span>}
              <GradePill grade={route.grade} aidGrade={route.aidGrade} obligatoryGrade={route.obligatoryGrade} />
            </span>
          </div>
          <p className="detail-head-row detail-meta">
            <span>{route.areaPath}</span>
            <span className="detail-head-facts">
              {route.orientation && (
                <span className="rc-meta-item">
                  <CompassRose size="sm" value={route.orientation as Orientation} />
                  {orientationAbbr[route.orientation as Orientation]}
                </span>
              )}
              {route.length != null && <span>{route.length} m</span>}
            </span>
          </p>
          {error && <p className="error-text">{error}</p>}
        </div>

        <div className="detail-layout">
          <div className="detail-main">
            {/* Grade, length, gear and orientation are all in the head now — nothing left
                to summarize before the description. */}
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

            {/* Only the photos live here; "+ Añadir topo" is in the rail with the other
                actions, so an empty section would be a bare heading. */}
            {route.topos.length > 0 && (
              <div className="detail-section">
                <h3>Topos</h3>
                <div className="topo-grid">
                  {route.topos.map((topo) => (
                    <div key={topo.id} className="topo-thumb">
                      <a href={topo.url} target="_blank" rel="noreferrer">
                        <img src={topo.url} alt={topo.fileName} loading="lazy" />
                      </a>
                      {/* Per photo, not per user: anyone may add a topo, but only whoever
                          uploaded it (or someone who can edit the route) may remove it — see
                          ToposController. The server answers that question so the button
                          isn't offered where the API would return 403. */}
                      {topo.canDelete && (
                        <RemoveButton variant="overlay" label="Eliminar" onClick={() => onDeleteTopo(topo.id)} />
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>

          <aside className="detail-side" aria-label="Acciones">
            {/* Review first when there's a decision pending: that's why a validator
                opened the route at all. */}
            {canReview && (
              <div className="detail-side-group">
                <h3>Revisión</h3>
                <Button variant="ghost" small fullWidth onClick={onValidate}>
                  <Icon name="check" size="sm" /> Validar
                </Button>
                <Button variant="ghost" small danger fullWidth onClick={() => setRejecting(true)}>
                  <Icon name="reject" size="sm" /> Rechazar
                </Button>
              </div>
            )}

            <div className="detail-side-group">
              {hasPermission(Permissions.tickCreate) && (
                <Button variant="primary" fullWidth onClick={() => setAddingTick(true)}>
                  {route.hasTick ? 'Registrar otro ascenso' : '+ Registrar ascenso'}
                </Button>
              )}
              <Button as="label" variant="ghost" small fullWidth>
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

            {canManage && (
              <div className="detail-side-group">
                <h3>Gestión</h3>
                {hasPermission(Permissions.routeEdit) && (
                  <Button variant="ghost" small fullWidth onClick={onEdit}>
                    <Icon name="edit" size="sm" /> Editar
                  </Button>
                )}
                {hasPermission(Permissions.routeDelete) && (
                  <Button variant="ghost" small danger fullWidth onClick={() => setConfirmingDelete(true)}>
                    <Icon name="delete" size="sm" /> Eliminar
                  </Button>
                )}
              </div>
            )}

            {onEnrich && hasPermission(Permissions.routeEnrich) && (
              <div className="detail-side-group">
                <h3>Búsqueda automática</h3>
                <Button
                  variant="ghost"
                  small
                  fullWidth
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
          </aside>
        </div>
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
