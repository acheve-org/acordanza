import { Navigate } from 'react-router';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';
import { AreaTree } from '../components/AreaTree';

export function AreasPage() {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.areaEdit);
  const canCreate = hasPermission(Permissions.areaCreate);
  const canDelete = hasPermission(Permissions.areaDelete);

  if (!canEdit) return <Navigate to="/" replace />;

  return (
    <>
      <h1>Sectores</h1>

      <section className="settings-section">
        <AreaTree canCreate={canCreate} canEdit={canEdit} canDelete={canDelete} />
      </section>
    </>
  );
}
