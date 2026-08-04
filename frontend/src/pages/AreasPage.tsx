import { useEffect, useState } from 'react';
import { Navigate } from 'react-router';
import * as areasApi from '../api/areas';
import type { AreaSummary } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';
import { AreaTree } from '../components/AreaTree';

export function AreasPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.areaEdit);
  const canCreate = hasPermission(Permissions.areaCreate);
  const canDelete = hasPermission(Permissions.areaDelete);

  const [areas, setAreas] = useState<AreaSummary[]>([]);
  const [error, setError] = useState('');

  // Feeds AreaTree with each area's polygon/parentAreaId so nodes can show whether they
  // already have a polygon and zoom to their parent's polygon when drawing one.
  function refresh() {
    return areasApi
      .listAreas()
      .then(setAreas)
      .catch(() => setError('No se pudieron cargar los sectores'));
  }

  useEffect(() => {
    if (!canEdit) return;
    refresh();
    // Only meant to run once, gated on the permission being available.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canEdit]);

  if (!canEdit) return <Navigate to="/" replace />;

  return (
    <>
      <h1>Sectores</h1>

      <section className="settings-section">
        <AreaTree canCreate={canCreate} canEdit={canEdit} canDelete={canDelete} areas={areas} onChange={refresh} />
      </section>

      {error && <p className="error-text">{error}</p>}
    </>
  );
}
