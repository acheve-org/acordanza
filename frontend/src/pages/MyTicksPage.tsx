import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { EmptyState } from 'acordanza-design-system';
import { AreaPicker } from '../components/AreaPicker';
import { TickedRouteCard } from '../components/TickedRouteCard';
import { DetailModal } from '../components/DetailModal';
import * as ticksApi from '../api/ticks';
import * as routesApi from '../api/routes';
import * as toposApi from '../api/topos';
import { gearLabels } from '../api/types';
import type { CreateTickInput, RouteDetail, TickedRoute, TickFilters } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';

const emptyFilters: TickFilters = { q: '', areaId: '', gear: '' };
const PAGE_SIZE = 20;

export function MyTicksPage() {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const [filters, setFilters] = useState<TickFilters>(emptyFilters);
  const [items, setItems] = useState<TickedRoute[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);

  const [detailRouteId, setDetailRouteId] = useState<number | null>(null);
  const [detailRoute, setDetailRoute] = useState<RouteDetail | null>(null);

  const sentinelRef = useRef<HTMLDivElement | null>(null);

  // Bumped on every request fired below; a response is only committed if it's
  // still the most recent one requested, so out-of-order network replies
  // (e.g. a fast retyped filter racing a slower earlier one) can't clobber
  // newer results.
  const requestIdRef = useRef(0);

  function set<K extends keyof TickFilters>(key: K, value: TickFilters[K]) {
    setFilters((f) => ({ ...f, [key]: value }));
  }

  useEffect(() => {
    const handle = setTimeout(() => {
      const requestId = ++requestIdRef.current;
      setLoading(true);
      ticksApi.listMyTicks(filters, 1, PAGE_SIZE).then((res) => {
        if (requestIdRef.current !== requestId) return;
        setItems(res.items);
        setTotalCount(res.totalCount);
        setPage(1);
        setLoading(false);
      });
    }, 250);
    return () => clearTimeout(handle);
  }, [filters]);

  const hasMore = items.length < totalCount;

  const loadMore = useCallback(() => {
    if (loading || !hasMore) return;
    const nextPage = page + 1;
    const requestId = ++requestIdRef.current;
    setLoading(true);
    ticksApi.listMyTicks(filters, nextPage, PAGE_SIZE).then((res) => {
      if (requestIdRef.current !== requestId) return;
      setItems((prev) => [...prev, ...res.items]);
      setTotalCount(res.totalCount);
      setPage(nextPage);
      setLoading(false);
    });
  }, [filters, page, loading, hasMore]);

  useEffect(() => {
    const el = sentinelRef.current;
    if (!el) return;
    const observer = new IntersectionObserver((entries) => {
      if (entries[0]?.isIntersecting) loadMore();
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, [loadMore]);

  useEffect(() => {
    if (detailRouteId === null) {
      setDetailRoute(null);
      return;
    }
    routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }, [detailRouteId]);

  function refreshDetail() {
    if (detailRouteId !== null) routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }

  function refreshList() {
    const requestId = ++requestIdRef.current;
    ticksApi.listMyTicks(filters, 1, PAGE_SIZE).then((res) => {
      if (requestIdRef.current !== requestId) return;
      setItems(res.items);
      setTotalCount(res.totalCount);
      setPage(1);
    });
  }

  async function handleDeleteTickRow(tickId: number) {
    await routesApi.deleteTick(tickId);
    refreshList();
  }

  async function handleAddTick(input: CreateTickInput) {
    if (!detailRouteId) return;
    await routesApi.createTick(detailRouteId, input);
    refreshDetail();
    refreshList();
  }

  async function handleUploadTopo(file: File) {
    if (!detailRouteId) return;
    await toposApi.uploadTopo(detailRouteId, file);
    refreshDetail();
  }

  async function handleDeleteTopo(topoId: number) {
    await toposApi.deleteTopo(topoId);
    refreshDetail();
  }

  async function handleValidateRoute() {
    if (!detailRouteId) return;
    await routesApi.validateRoute(detailRouteId);
    refreshDetail();
  }

  async function handleRejectRoute(reason: string) {
    if (!detailRouteId) return;
    await routesApi.rejectRoute(detailRouteId, reason);
    refreshDetail();
  }

  async function handleDeleteRoute() {
    if (!detailRouteId) return;
    await routesApi.deleteRoute(detailRouteId);
    setDetailRouteId(null);
    refreshList();
  }

  return (
    <>
      <main className="layout">
        <aside className="filters">
          <h2>Buscar</h2>
          <input
            type="text"
            placeholder="Nombre de la vía o descripción…"
            value={filters.q}
            onChange={(e) => set('q', e.target.value)}
          />

          <label className="field-label">Sector</label>
          <AreaPicker
            value={filters.areaId ? Number(filters.areaId) : null}
            onChange={(id) => set('areaId', id === null ? '' : String(id))}
            allowClear
          />

          <label className="field-label">Equipamiento</label>
          <select value={filters.gear} onChange={(e) => set('gear', e.target.value)}>
            <option value="">Cualquiera</option>
            {Object.entries(gearLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </aside>

        <section className="routes-panel">
          <div className="routes-header">
            <h1>Mis vías ({totalCount})</h1>
          </div>
          <div className="routes-list">
            {items.map((t) => (
              <TickedRouteCard
                key={t.tickId}
                tick={t}
                onSelect={() => setDetailRouteId(t.routeId)}
                onDelete={hasPermission(Permissions.tickDelete) ? () => handleDeleteTickRow(t.tickId) : undefined}
              />
            ))}
          </div>
          {items.length === 0 && !loading && (
            <EmptyState>Todavía no has registrado ningún ascenso.</EmptyState>
          )}
          <div ref={sentinelRef} />
          {loading && (
            <p className="hint" style={{ textAlign: 'center', marginTop: 'var(--space-md)' }}>
              Cargando…
            </p>
          )}
        </section>
      </main>

      <DetailModal
        route={detailRoute}
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
      />
    </>
  );
}
