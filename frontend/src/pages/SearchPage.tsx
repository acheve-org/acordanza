import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Filters } from '../components/Filters';
import { RoutesList } from '../components/RoutesList';
import { DetailModal } from '../components/DetailModal';
import * as routesApi from '../api/routes';
import * as toposApi from '../api/topos';
import { queryKeys } from '../api/queryKeys';
import { errorMessage } from '../api/errorMessage';
import type { CreateTickInput, RouteFilters } from '../api/types';
import { emptyRouteFilters } from '../api/types';

export function SearchPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [filters, setFilters] = useState<RouteFilters>(emptyRouteFilters);
  const [debouncedFilters, setDebouncedFilters] = useState(filters);
  const [detailRouteId, setDetailRouteId] = useState<number | null>(null);
  const [actionError, setActionError] = useState('');

  useEffect(() => {
    const handle = setTimeout(() => setDebouncedFilters(filters), 250);
    return () => clearTimeout(handle);
  }, [filters]);

  const { data: routes = [], error: routesError } = useQuery({
    queryKey: queryKeys.routes(debouncedFilters),
    queryFn: () => routesApi.listRoutes(debouncedFilters),
  });

  const { data: detailRoute } = useQuery({
    queryKey: queryKeys.route(detailRouteId ?? 0),
    queryFn: () => routesApi.getRoute(detailRouteId!),
    enabled: detailRouteId !== null,
  });

  function invalidateRouteQueries() {
    queryClient.invalidateQueries({ queryKey: ['routes'] });
    if (detailRouteId !== null) queryClient.invalidateQueries({ queryKey: queryKeys.route(detailRouteId) });
  }

  const deleteRouteMutation = useMutation({
    mutationFn: (id: number) => routesApi.deleteRoute(id),
    onSuccess: () => {
      setDetailRouteId(null);
      queryClient.invalidateQueries({ queryKey: ['routes'] });
    },
    onError: (err) => setActionError(errorMessage(err, 'No se pudo eliminar la vía')),
  });

  const validateRouteMutation = useMutation({
    mutationFn: (id: number) => routesApi.validateRoute(id),
    onSuccess: invalidateRouteQueries,
    onError: (err) => setActionError(errorMessage(err, 'No se pudo validar la vía')),
  });

  const rejectRouteMutation = useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) => routesApi.rejectRoute(id, reason),
    onSuccess: invalidateRouteQueries,
  });

  const addTickMutation = useMutation({
    mutationFn: ({ routeId, input }: { routeId: number; input: CreateTickInput }) =>
      routesApi.createTick(routeId, input),
    onSuccess: invalidateRouteQueries,
  });

  const uploadTopoMutation = useMutation({
    mutationFn: ({ routeId, file }: { routeId: number; file: File }) => toposApi.uploadTopo(routeId, file),
    onSuccess: invalidateRouteQueries,
  });

  const deleteTopoMutation = useMutation({
    mutationFn: (topoId: number) => toposApi.deleteTopo(topoId),
    onSuccess: invalidateRouteQueries,
    onError: (err) => setActionError(errorMessage(err, 'No se pudo eliminar el topo')),
  });

  const enrichRouteMutation = useMutation({
    mutationFn: ({ id, force }: { id: number; force: boolean }) => routesApi.enrichRoute(id, force),
  });

  async function handleDeleteRoute() {
    if (!detailRouteId) return;
    setActionError('');
    await deleteRouteMutation.mutateAsync(detailRouteId);
  }

  async function handleAddTick(input: CreateTickInput) {
    if (!detailRouteId) return;
    // AddTickModal shows its own error locally, so let the rejection propagate.
    await addTickMutation.mutateAsync({ routeId: detailRouteId, input });
  }

  async function handleUploadTopo(file: File) {
    if (!detailRouteId) return;
    // DetailModal shows its own upload error locally, so let the rejection propagate.
    await uploadTopoMutation.mutateAsync({ routeId: detailRouteId, file });
  }

  async function handleDeleteTopo(topoId: number) {
    setActionError('');
    await deleteTopoMutation.mutateAsync(topoId);
  }

  async function handleValidateRoute() {
    if (!detailRouteId) return;
    setActionError('');
    await validateRouteMutation.mutateAsync(detailRouteId);
  }

  async function handleRejectRoute(reason: string) {
    if (!detailRouteId) return;
    // RejectReasonModal shows its own error locally, so let the rejection propagate.
    await rejectRouteMutation.mutateAsync({ id: detailRouteId, reason });
  }

  async function handleEnrichRoute(force: boolean) {
    if (!detailRouteId) return;
    // DetailModal shows its own status/error locally, so let the rejection propagate.
    await enrichRouteMutation.mutateAsync({ id: detailRouteId, force });
  }

  return (
    <>
      <main className="layout">
        <Filters filters={filters} onChange={setFilters} onAddRoute={() => navigate('/routes/new')} />
        {routesError ? (
          <p className="error-text">{errorMessage(routesError, 'No se pudieron cargar las vías')}</p>
        ) : (
          <RoutesList routes={routes} onSelect={setDetailRouteId} />
        )}
      </main>

      <DetailModal
        route={detailRoute ?? null}
        error={actionError}
        onClose={() => setDetailRouteId(null)}
        onEdit={() => {
          if (detailRouteId !== null) navigate(`/routes/${detailRouteId}/edit`);
        }}
        onDelete={handleDeleteRoute}
        onValidate={handleValidateRoute}
        onReject={handleRejectRoute}
        onAddTick={handleAddTick}
        onUploadTopo={handleUploadTopo}
        onDeleteTopo={handleDeleteTopo}
        onEnrich={handleEnrichRoute}
      />
    </>
  );
}
