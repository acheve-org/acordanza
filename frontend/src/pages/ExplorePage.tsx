import { lazy, Suspense, useState } from 'react';
import { useNavigate } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { EmptyState, List, ListRow, Pagination } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import { queryKeys } from '../api/queryKeys';

const AreaMap = lazy(() => import('../components/AreaMap').then((m) => ({ default: m.AreaMap })));

const PAGE_SIZE = 10;

export function ExplorePage() {
  const navigate = useNavigate();
  const [page, setPage] = useState(1);

  const { data } = useQuery({
    queryKey: queryKeys.rootAreas(page),
    queryFn: () => areasApi.listRootAreas(page, PAGE_SIZE),
    placeholderData: keepPreviousData,
  });

  const zones = data?.items ?? [];
  const pageCount = Math.max(1, Math.ceil((data?.totalCount ?? 0) / PAGE_SIZE));

  return (
    <main className="explore-page">
      <h1>Explora</h1>
      <p className="hint">
        Encuentra zonas que no conoces en el mapa, y descubre los sectores que las componen.
      </p>

      <Suspense fallback={<div className="area-map-placeholder" />}>
        <AreaMap />
      </Suspense>
      <p className="hint">Solo se muestran en el mapa los sectores que tienen información geográfica cargada.</p>

      <section className="settings-section">
        <h2>Zonas</h2>
        {zones.length === 0 ? (
          <EmptyState>Todavía no hay ninguna zona.</EmptyState>
        ) : (
          <>
            <List>
              {zones.map((zone) => (
                <ListRow
                  key={zone.id}
                  title={zone.name}
                  subtitle={zone.hasChildren ? 'Con sectores' : 'Sin sectores'}
                  onClick={() => navigate(`/areas/${zone.slug}`)}
                />
              ))}
            </List>
            {pageCount > 1 && <Pagination page={page} pageCount={pageCount} onPageChange={setPage} />}
          </>
        )}
      </section>
    </main>
  );
}
