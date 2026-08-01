import { Link } from 'react-router';
import { AreaMap } from '../components/AreaMap';

export function MapPage() {
  return (
    <main className="map-page">
      <h1>Mapa</h1>
      <p className="hint">Solo se muestran los sectores que tienen información geográfica cargada.</p>
      <AreaMap />
      <Link to="/" className="ghost-btn small">
        Volver
      </Link>
    </main>
  );
}
