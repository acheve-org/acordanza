import { Fragment, type ChangeEvent } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, EmptyState, Icon, List, ListRow, RemoveButton } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import * as routesApi from '../api/routes';
import { queryKeys } from '../api/queryKeys';
import { ApiError } from '../api/client';
import type { RouteSummary } from '../api/types';
import { emptyRouteFilters } from '../api/types';
import { formatDate } from '../format';
import { RoutesList } from '../components/RoutesList';

const ACCEPTED_MEDIA_TYPES = 'image/jpeg,image/png,image/webp';

export function AreaPage() {
  const params = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const path = params['*'] ?? '';
  const queryClient = useQueryClient();

  const { data: area, error } = useQuery({
    queryKey: queryKeys.areaBySlugPath(path),
    queryFn: () => areasApi.getAreaBySlugPath(path),
  });

  const { data: routes = [] } = useQuery({
    queryKey: queryKeys.routesByArea(area?.id ?? 0),
    queryFn: () => routesApi.listRoutes({ ...emptyRouteFilters, areaId: String(area!.id) }, true),
    enabled: !!area,
  });

  function invalidateArea() {
    queryClient.invalidateQueries({ queryKey: queryKeys.areaBySlugPath(path) });
  }

  const uploadMediaMutation = useMutation({
    mutationFn: async (files: File[]) => {
      for (const file of files) {
        await areasApi.uploadAreaMedia(area!.id, file);
      }
    },
    onSuccess: invalidateArea,
  });

  const deleteMediaMutation = useMutation({
    mutationFn: (mediaId: number) => areasApi.deleteAreaMedia(mediaId),
    onSuccess: invalidateArea,
  });

  function handleSelectRoute(route: RouteSummary) {
    navigate(routesApi.routeDetailPath(route), { state: { backgroundLocation: location } });
  }

  async function handleMediaFilesSelected(e: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';
    if (files.length === 0) return;
    await uploadMediaMutation.mutateAsync(files).catch(() => {});
  }

  function handleDeleteMedia(mediaId: number) {
    deleteMediaMutation.mutate(mediaId);
  }

  const notFound = error instanceof ApiError && error.status === 404;

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
          <Icon name="check" size="sm" /> Validado por {area.decidedByName} el {formatDate(area.decidedAt!)}
        </p>
      )}
      {area.validationStatus === 'rejected' && (
        <p className="detail-fa">
          <Icon name="reject" size="sm" /> Rechazado por {area.decidedByName} el {formatDate(area.decidedAt!)}
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
            {uploadMediaMutation.isPending ? 'Subiendo…' : '+ Añadir foto'}
            <input
              type="file"
              accept={ACCEPTED_MEDIA_TYPES}
              multiple
              disabled={uploadMediaMutation.isPending}
              onChange={handleMediaFilesSelected}
              className="hidden"
            />
          </Button>
        )}
        {uploadMediaMutation.isError && <p className="error-text">No se pudo subir la imagen</p>}
      </section>
    </main>
  );
}
