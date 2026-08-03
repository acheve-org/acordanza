import { useEffect, useState } from 'react';
import * as areasApi from '../api/areas';
import type { AreaNode, PagedAreaNodes } from '../api/types';
import { ApiError } from '../api/client';
import { AreaNameModal } from './AreaNameModal';

const PAGE_SIZE = 10;

interface CreateModalState {
  parentAreaId: number | null;
  onCreated: () => void;
}

interface AreaTreeNodeProps {
  node: AreaNode;
  canCreate: boolean;
  canDelete: boolean;
  onError: (message: string) => void;
  onRequestCreate: (parentAreaId: number, onCreated: () => void) => void;
  onDeleted: () => void;
  onChange: () => void;
}

function AreaTreeNode({ node, canCreate, canDelete, onError, onRequestCreate, onDeleted, onChange }: AreaTreeNodeProps) {
  const [expanded, setExpanded] = useState(false);
  const [children, setChildren] = useState<AreaNode[] | null>(null);
  const [loadingChildren, setLoadingChildren] = useState(false);
  const [hasChildren, setHasChildren] = useState(node.hasChildren);

  function loadChildren() {
    setLoadingChildren(true);
    areasApi
      .listAreaChildren(node.id)
      .then((items) => {
        setChildren(items);
        setHasChildren(items.length > 0);
      })
      .catch((err) => onError(err instanceof ApiError ? err.message : 'No se pudieron cargar los subsectores'))
      .finally(() => setLoadingChildren(false));
  }

  function toggleExpand() {
    if (!expanded && children === null) {
      loadChildren();
    }
    setExpanded((e) => !e);
  }

  async function handleDelete() {
    if (!confirm(`¿Eliminar el sector "${node.name}" y todos sus subsectores?`)) return;
    try {
      await areasApi.deleteArea(node.id);
      onDeleted();
      onChange();
    } catch (err) {
      onError(err instanceof ApiError ? err.message : 'No se pudo eliminar el sector');
    }
  }

  return (
    <li className="area-tree-node">
      <div className="area-tree-row">
        {hasChildren ? (
          <button
            type="button"
            className="area-tree-toggle"
            onClick={toggleExpand}
            aria-label={expanded ? 'Contraer' : 'Expandir'}
          >
            {expanded ? '▾' : '▸'}
          </button>
        ) : (
          <span className="area-tree-toggle-spacer" />
        )}
        <span className="area-tree-name">{node.name}</span>
        <div className="detail-actions">
          {canCreate && (
            <button
              type="button"
              className="ghost-btn small"
              onClick={() =>
                onRequestCreate(node.id, () => {
                  loadChildren();
                  setExpanded(true);
                })
              }
            >
              + Subsector
            </button>
          )}
          {canDelete && (
            <button type="button" className="ghost-btn small danger" onClick={handleDelete}>
              Eliminar
            </button>
          )}
        </div>
      </div>
      {expanded && (
        <ul className="area-tree-children">
          {loadingChildren && <li className="hint">Cargando…</li>}
          {!loadingChildren && children?.length === 0 && <li className="hint">Sin subsectores</li>}
          {!loadingChildren &&
            children?.map((child) => (
              <AreaTreeNode
                key={child.id}
                node={child}
                canCreate={canCreate}
                canDelete={canDelete}
                onError={onError}
                onRequestCreate={onRequestCreate}
                onDeleted={loadChildren}
                onChange={onChange}
              />
            ))}
        </ul>
      )}
    </li>
  );
}

interface Props {
  canCreate: boolean;
  canDelete: boolean;
  // Notified after any create/delete anywhere in the tree, so callers whose own state
  // mirrors the area list (e.g. the polygon-drawing section's AreaPicker/selection) can
  // stay in sync instead of silently going stale.
  onChange: () => void;
}

export function AreaTree({ canCreate, canDelete, onChange }: Props) {
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedAreaNodes>({ items: [], totalCount: 0, page: 1, pageSize: PAGE_SIZE });
  const [error, setError] = useState('');
  const [createModal, setCreateModal] = useState<CreateModalState | null>(null);

  function refreshRoots() {
    areasApi
      .listRootAreas(page, PAGE_SIZE)
      .then((r) => {
        const totalPages = Math.max(1, Math.ceil(r.totalCount / r.pageSize));
        if (page > totalPages) {
          setPage(totalPages);
          return;
        }
        setResult(r);
        setError('');
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : 'No se pudieron cargar los sectores'));
  }

  useEffect(() => {
    refreshRoots();
    // Only meant to re-run when the page changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page]);

  function openCreateModal(parentAreaId: number | null, onCreated: () => void) {
    setError('');
    setCreateModal({ parentAreaId, onCreated });
  }

  function handleCreated() {
    createModal?.onCreated();
    setCreateModal(null);
    onChange();
  }

  const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));

  return (
    <div className="area-tree-wrap">
      <div className="area-tree-header">
        {canCreate && (
          <button type="button" className="primary-btn" onClick={() => openCreateModal(null, refreshRoots)}>
            Añadir sector principal
          </button>
        )}
      </div>

      {error && <p className="error-text">{error}</p>}

      {result.items.length === 0 ? (
        <p className="empty-state">No hay sectores todavía</p>
      ) : (
        <ul className="area-tree">
          {result.items.map((node) => (
            <AreaTreeNode
              key={node.id}
              node={node}
              canCreate={canCreate}
              canDelete={canDelete}
              onError={setError}
              onRequestCreate={openCreateModal}
              onDeleted={refreshRoots}
              onChange={onChange}
            />
          ))}
        </ul>
      )}

      <div className="pagination">
        <button type="button" className="ghost-btn small" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
          Anterior
        </button>
        <span className="pagination-status">
          Página {page} de {totalPages}
        </span>
        <button
          type="button"
          className="ghost-btn small"
          disabled={page >= totalPages}
          onClick={() => setPage((p) => p + 1)}
        >
          Siguiente
        </button>
      </div>

      {createModal && (
        <AreaNameModal parentAreaId={createModal.parentAreaId} onClose={() => setCreateModal(null)} onCreated={handleCreated} />
      )}
    </div>
  );
}
