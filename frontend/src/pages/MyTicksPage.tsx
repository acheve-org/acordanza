import { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { EmptyState } from 'acordanza-design-system';
import { AreaPicker } from '../components/AreaPicker';
import { TickedRouteCard } from '../components/TickedRouteCard';
import * as ticksApi from '../api/ticks';
import * as routesApi from '../api/routes';
import { queryKeys } from '../api/queryKeys';
import { gearLabels } from '../api/types';
import type { TickedRoute, TickFilters } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';
import { useDebouncedValue } from '../hooks/useDebouncedValue';

const emptyFilters: TickFilters = { q: '', areaId: '', gear: '' };
const PAGE_SIZE = 20;

export function MyTicksPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { hasPermission } = useAuth();
  const queryClient = useQueryClient();
  const [filters, setFilters] = useState<TickFilters>(emptyFilters);

  const sentinelRef = useRef<HTMLDivElement | null>(null);

  function set<K extends keyof TickFilters>(key: K, value: TickFilters[K]) {
    setFilters((f) => ({ ...f, [key]: value }));
  }

  const debouncedFilters = useDebouncedValue(filters);

  const { data, isFetching, hasNextPage, isFetchingNextPage, fetchNextPage } = useInfiniteQuery({
    queryKey: queryKeys.myTicks(debouncedFilters),
    queryFn: ({ pageParam }) => ticksApi.listMyTicks(debouncedFilters, pageParam, PAGE_SIZE),
    initialPageParam: 1,
    getNextPageParam: (_lastPage, allPages) => {
      const loaded = allPages.reduce((sum, p) => sum + p.items.length, 0);
      const totalCount = allPages[0]?.totalCount ?? 0;
      return loaded < totalCount ? allPages.length + 1 : undefined;
    },
  });

  const items = data?.pages.flatMap((p) => p.items) ?? [];
  const totalCount = data?.pages[0]?.totalCount ?? 0;
  const loading = isFetching && !isFetchingNextPage;

  const loadMore = useCallback(() => {
    if (isFetchingNextPage || !hasNextPage) return;
    fetchNextPage();
  }, [isFetchingNextPage, hasNextPage, fetchNextPage]);

  useEffect(() => {
    const el = sentinelRef.current;
    if (!el) return;
    const observer = new IntersectionObserver((entries) => {
      if (entries[0]?.isIntersecting) loadMore();
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, [loadMore]);

  const deleteTickMutation = useMutation({
    mutationFn: (tickId: number) => routesApi.deleteTick(tickId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ticks', 'mine'] }),
  });

  function handleSelectTick(tick: TickedRoute) {
    navigate(`/routes/${tick.areaSlugPath}/${tick.routeSlug}`, { state: { backgroundLocation: location } });
  }

  return (
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
              onSelect={() => handleSelectTick(t)}
              onDelete={
                hasPermission(Permissions.tickDelete) ? () => deleteTickMutation.mutate(t.tickId) : undefined
              }
            />
          ))}
        </div>
        {items.length === 0 && !loading && <EmptyState>Todavía no has registrado ningún ascenso.</EmptyState>}
        <div ref={sentinelRef} />
        {loading && (
          <p className="hint" style={{ textAlign: 'center', marginTop: 'var(--space-md)' }}>
            Cargando…
          </p>
        )}
      </section>
    </main>
  );
}
