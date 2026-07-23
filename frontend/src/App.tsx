import { useEffect, useRef, useState } from 'react';
import { useAuth } from './context/AuthContext';
import { LoginScreen } from './components/LoginScreen';
import { Filters } from './components/Filters';
import { RoutesList } from './components/RoutesList';
import { RouteFormModal } from './components/RouteFormModal';
import { DetailModal } from './components/DetailModal';
import * as routesApi from './api/routes';
import type { CreateTickInput, RouteDetail, RouteFilters, RouteSummary, UpsertRouteInput } from './api/types';

const emptyFilters: RouteFilters = { q: '', crag: '', type: '', tag: '' };

function App() {
  const { username, loading, logout } = useAuth();

  const [filters, setFilters] = useState<RouteFilters>(emptyFilters);
  const [routes, setRoutes] = useState<RouteSummary[]>([]);

  const [routeModalOpen, setRouteModalOpen] = useState(false);
  const [editingRoute, setEditingRoute] = useState<RouteSummary | RouteDetail | null>(null);

  const [detailRouteId, setDetailRouteId] = useState<number | null>(null);
  const [detailRoute, setDetailRoute] = useState<RouteDetail | null>(null);

  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!userMenuOpen) return;
    function handleClickOutside(e: MouseEvent) {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target as Node)) {
        setUserMenuOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [userMenuOpen]);

  useEffect(() => {
    if (!username) return;
    const handle = setTimeout(() => {
      routesApi.listRoutes(filters).then(setRoutes);
    }, 250);
    return () => clearTimeout(handle);
  }, [filters, username]);

  useEffect(() => {
    if (detailRouteId === null) {
      setDetailRoute(null);
      return;
    }
    routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }, [detailRouteId]);

  function refreshRoutes() {
    routesApi.listRoutes(filters).then(setRoutes);
  }

  function refreshDetail() {
    if (detailRouteId !== null) routesApi.getRoute(detailRouteId).then(setDetailRoute);
  }

  async function handleSaveRoute(input: UpsertRouteInput) {
    if (editingRoute) {
      await routesApi.updateRoute(editingRoute.id, input);
    } else {
      await routesApi.createRoute(input);
    }
    setRouteModalOpen(false);
    setEditingRoute(null);
    refreshRoutes();
    refreshDetail();
  }

  async function handleDeleteRoute() {
    if (!detailRouteId) return;
    if (!confirm('¿Eliminar esta vía y todos sus ascensos registrados?')) return;
    await routesApi.deleteRoute(detailRouteId);
    setDetailRouteId(null);
    refreshRoutes();
  }

  async function handleAddTick(input: CreateTickInput) {
    if (!detailRouteId) return;
    await routesApi.createTick(detailRouteId, input);
    refreshDetail();
    refreshRoutes();
  }

  async function handleDeleteTick(tickId: number) {
    await routesApi.deleteTick(tickId);
    refreshDetail();
  }

  if (loading) return null;
  if (!username) return <LoginScreen />;

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark">&#9670;</span>
          <span className="brand-name">Acordanza</span>
        </div>
        <div className="topbar-right">
          <div className="user-menu" ref={userMenuRef}>
            <button
              className="user-menu-trigger"
              aria-label={username}
              title={username}
              aria-haspopup="true"
              aria-expanded={userMenuOpen}
              onClick={() => setUserMenuOpen((open) => !open)}
            >
              <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <circle cx="12" cy="8" r="4" />
                <path d="M4 20c0-4.418 3.582-8 8-8s8 3.582 8 8v1H4v-1z" />
              </svg>
            </button>
            {userMenuOpen && (
              <div className="user-menu-popup">
                <span className="user-menu-name">{username}</span>
                <button
                  className="ghost-btn small full-width"
                  onClick={() => {
                    setUserMenuOpen(false);
                    logout();
                  }}
                >
                  Cerrar sesión
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      <main className="layout">
        <Filters
          filters={filters}
          onChange={setFilters}
          onAddRoute={() => {
            setEditingRoute(null);
            setRouteModalOpen(true);
          }}
        />
        <RoutesList routes={routes} onSelect={setDetailRouteId} />
      </main>

      <RouteFormModal
        open={routeModalOpen}
        editing={editingRoute}
        onCancel={() => {
          setRouteModalOpen(false);
          setEditingRoute(null);
        }}
        onSave={handleSaveRoute}
      />

      <DetailModal
        route={detailRoute}
        onClose={() => setDetailRouteId(null)}
        onEdit={() => {
          setEditingRoute(detailRoute);
          setDetailRouteId(null);
          setRouteModalOpen(true);
        }}
        onDelete={handleDeleteRoute}
        onAddTick={handleAddTick}
        onDeleteTick={handleDeleteTick}
      />
    </div>
  );
}

export default App;
