import { useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { DetailModal } from './DetailModal';
import * as routesApi from '../api/routes';
import * as toposApi from '../api/topos';
import { errorMessage } from '../api/errorMessage';
import type { BackgroundLocationState } from '../api/backgroundLocation';
import type { CreateTickInput } from '../api/types';

// The sole place a route's detail is ever fetched or mutated from now on (plan 21) —
// rendered by App.tsx's background-location overlay <Routes> for every one of the six
// contexts that used to hand-roll this wiring themselves (SearchPage, AreaPage,
// FirstAscensionistPage, MyOpeningsPage, PendingTasksPage, MyTicksPage). Reads the full
// {area-slug-path}/{route-slug} match via useParams()['*'], fetches by that path, and
// closes by navigating back to the backgroundLocation state the opener attached to the
// link (falling back to /routes if it's somehow missing).
export function RouteDetailOverlay() {
  const navigate = useNavigate();
  const location = useLocation();
  const path = useParams()['*'] ?? '';
  const queryClient = useQueryClient();
  const [actionError, setActionError] = useState('');

  const backgroundLocation = (location.state as BackgroundLocationState | null)?.backgroundLocation;

  function close() {
    navigate(backgroundLocation ?? '/routes');
  }

  const { data: route } = useQuery({
    queryKey: ['route', 'by-slug-path', path],
    queryFn: () => routesApi.getRouteBySlugPath(path),
    enabled: path.length > 0,
  });

  function invalidateRouteQueries() {
    queryClient.invalidateQueries({ queryKey: ['routes'] });
    queryClient.invalidateQueries({ queryKey: ['route', 'by-slug-path', path] });
  }

  const deleteRouteMutation = useMutation({
    mutationFn: (id: number) => routesApi.deleteRoute(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['routes'] });
      close();
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

  async function handleDeleteRoute() {
    if (!route) return;
    setActionError('');
    await deleteRouteMutation.mutateAsync(route.id);
  }

  async function handleAddTick(input: CreateTickInput) {
    if (!route) return;
    // AddTickModal shows its own error locally, so let the rejection propagate.
    await addTickMutation.mutateAsync({ routeId: route.id, input });
  }

  async function handleUploadTopo(file: File) {
    if (!route) return;
    // DetailModal shows its own upload error locally, so let the rejection propagate.
    await uploadTopoMutation.mutateAsync({ routeId: route.id, file });
  }

  async function handleDeleteTopo(topoId: number) {
    setActionError('');
    await deleteTopoMutation.mutateAsync(topoId);
  }

  async function handleValidateRoute() {
    if (!route) return;
    setActionError('');
    await validateRouteMutation.mutateAsync(route.id);
  }

  async function handleRejectRoute(reason: string) {
    if (!route) return;
    // RejectReasonModal shows its own error locally, so let the rejection propagate.
    await rejectRouteMutation.mutateAsync({ id: route.id, reason });
  }

  return (
    <DetailModal
      route={route ?? null}
      error={actionError}
      onClose={close}
      onEdit={() => {
        if (route) navigate(`/routes/${route.id}/edit`);
      }}
      onDelete={handleDeleteRoute}
      onValidate={handleValidateRoute}
      onReject={handleRejectRoute}
      onAddTick={handleAddTick}
      onUploadTopo={handleUploadTopo}
      onDeleteTopo={handleDeleteTopo}
    />
  );
}
