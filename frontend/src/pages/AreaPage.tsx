import { Fragment, useEffect, useState, type ChangeEvent } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import { Button, EmptyState, List, ListRow, RemoveButton } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import * as routesApi from '../api/routes';
import type { AreaDetail, RouteSummary } from '../api/types';
import { emptyRouteFilters } from '../api/types';
import { formatDate } from '../format';
import { RoutesList } from '../components/RoutesList';

const ACCEPTED_MEDIA_TYPES = 'image/jpeg,image/png,image/webp';

export function AreaPage() {
  const params = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const path = params['*'] ?? '';

  const [area, setArea] = useState<AreaDetail | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [routes, setRoutes] = useState<RouteSummary[]>([]);

  const [uploadingMedia, setUploadingMedia] = useState(false);
  const [mediaError, setMediaError] = useState('');

  function refreshArea() {
    areasApi
      .getAreaBySlugPath(path)
      .then((a) => {
        setArea(a);
        setNotFound(false);
      })
      .catch(() => setNotFound(true));
  }

  function refreshRoutes(areaId: number) {
    routesApi.listRoutes({ ...emptyRouteFilters, areaId: String(areaId) }, true).then(setRoutes);
  }

  useEffect(() => {
    setArea(null);
    setNotFound(false);
    refreshArea();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path]);

  useEffect(() => {
    if (area) refreshRoutes(area.id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [area?.id]);

  function handleSelectRoute(route: RouteSummary) {
    navigate(routesApi.routeDetailPath(route), { state: { backgroundLocation: location } });
  }

  async function handleMediaFilesSelected(e: ChangeEvent<HTMLInputElement>) {
    if (!area) return;
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';
    if (files.length === 0) return;

    setUploadingMedia(true);
    setMediaError('');
    try {
      for (const file of files) {
        await areasApi.uploadAreaMedia(area.id, file);
      }
      refreshArea();
    } catch {
      setMediaError('No se pudo subir la imagen');
    } finally {
      setUploadingMedia(false);
    }
  }

  async function handleDeleteMedia(mediaId: number) {
    await areasApi.deleteAreaMedia(mediaId);
    refreshArea();
  }

  if (notFound) {
    return (
      <main className="area-page">
        <EmptyState>Este sector no existe.</EmptyState>
        <Button as={Link} variant="ghost" small to="/explore">
          Volver a explorar
        </Button>
      </main>
    );
  }

  if (!area) return null;

  const breadcrumbSegments = area.ancestors.map((a, i) => ({
    ...a,
    path: area.ancestors
      .slice(0, i + 1)
      .map((x) => x.slug)
      .join('/'),
  }));

  return (
    <main className="area-page">
      <nav className="area-breadcrumb" aria-label="Ruta del sector">
        {breadcrumbSegments.map((segment) => (
          <Fragment key={segment.id}>
            <Link to={`/areas/${segment.path}`}>{segment.name}</Link>
            <span className="area-breadcrumb-sep">›</span>
          </Fragment>
        ))}
        <span className="area-breadcrumb-current">{area.name}</span>
      </nav>

      <h1>{area.name}</h1>
      {area.validationStatus === 'approved' && (
        <p className="detail-fa">
          ✓ Validado por {area.decidedByName} el {formatDate(area.decidedAt!)}
        </p>
      )}
      {area.validationStatus === 'rejected' && (
        <p className="detail-fa">
          ✗ Rechazado por {area.decidedByName} el {formatDate(area.decidedAt!)}
          {area.rejectionReason && ` · Motivo: ${area.rejectionReason}`}
        </p>
      )}

      <section className="settings-section">
        <h2>Subsectores</h2>
        {area.children.length === 0 ? (
          <EmptyState>Este sector no tiene subsectores.</EmptyState>
        ) : (
          <List>
            {area.children.map((child) => (
              <ListRow
                key={child.id}
                title={child.name}
                onClick={() => navigate(`/areas/${path ? `${path}/` : ''}${child.slug}`)}
              />
            ))}
          </List>
        )}
      </section>

      <RoutesList routes={routes} onSelect={handleSelectRoute} />

      <section className="settings-section">
        <h2>Fotos del sector</h2>
        {area.media.length > 0 && (
          <div className="topo-grid">
            {area.media.map((m) => (
              <div key={m.id} className="topo-thumb">
                <a href={m.url} target="_blank" rel="noreferrer">
                  <img src={m.url} alt={m.fileName} loading="lazy" />
                </a>
                {area.canManageMedia && (
                  <RemoveButton variant="overlay" label="Eliminar" onClick={() => handleDeleteMedia(m.id)} />
                )}
              </div>
            ))}
          </div>
        )}
        {area.media.length === 0 && <EmptyState>Este sector todavía no tiene fotos.</EmptyState>}
        {area.canManageMedia && (
          <Button as="label" variant="ghost" small className="topo-upload">
            {uploadingMedia ? 'Subiendo…' : '+ Añadir foto'}
            <input
              type="file"
              accept={ACCEPTED_MEDIA_TYPES}
              multiple
              disabled={uploadingMedia}
              onChange={handleMediaFilesSelected}
              className="hidden"
            />
          </Button>
        )}
        {mediaError && <p className="error-text">{mediaError}</p>}
      </section>
    </main>
  );
}
