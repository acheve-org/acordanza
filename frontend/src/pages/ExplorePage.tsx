import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { EmptyState, List, ListRow, Pagination } from 'acordanza-design-system';
import { AreaMap } from '../components/AreaMap';
import * as areasApi from '../api/areas';
import type { AreaNode } from '../api/types';

const PAGE_SIZE = 10;

export function ExplorePage() {
  const navigate = useNavigate();
  const [zones, setZones] = useState<AreaNode[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);

  useEffect(() => {
    areasApi.listRootAreas(page, PAGE_SIZE).then((paged) => {
      setZones(paged.items);
      setTotalCount(paged.totalCount);
    });
  }, [page]);

  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <main className="explore-page">
      <h1>Explora</h1>
      <p className="hint">
        Encuentra zonas que no conoces en el mapa, y descubre los sectores que las componen.
      </p>

      <AreaMap />
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
