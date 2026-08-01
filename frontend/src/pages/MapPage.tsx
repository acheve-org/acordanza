import { Link } from 'react-router';
import { AreaMap } from '../components/AreaMap';
import { Permissions } from '../api/permissions';
import { useAuth } from '../context/AuthContext';

export function MapPage() {
  const { hasPermission } = useAuth();

  return (
    <main className="map-page">
      <h1>Mapa</h1>
      <AreaMap canEdit={hasPermission(Permissions.areaEdit)} />
      <Link to="/" className="ghost-btn small">
        Volver
      </Link>
    </main>
  );
}
