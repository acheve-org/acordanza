import { lazy, Suspense, useEffect, useState } from 'react';
import { Button, ConfirmModal, EmptyState, Pagination } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import type { AreaNode, PagedAreaNodes } from '../api/types';
import { errorMessage } from '../api/errorMessage';
import { AreaNameModal } from './AreaNameModal';

const AreaPolygonModal = lazy(() => import('./AreaPolygonModal').then((m) => ({ default: m.AreaPolygonModal })));

const PAGE_SIZE = 10;

interface CreateModalState {
  parentAreaId: number | null;
  onCreated: () => void;
}

interface AreaTreeNodeProps {
  node: AreaNode;
  parentAreaId: number | null;
  canCreate: boolean;
  canEdit: boolean;
  canDelete: boolean;
  onError: (message: string) => void;
  onRequestCreate: (parentAreaId: number, onCreated: () => void) => void;
  // Reloads the node list at this level (this node's siblings) — needed after a delete
  // (the node disappears), a rename, or a polygon save, since `node` here is sourced from
  // that list and carries the name/hasPolygon the row renders.
  onListChanged: () => void;
}

function AreaTreeNode({
  node,
  parentAreaId,
  canCreate,
  canEdit,
  canDelete,
  onError,
  onRequestCreate,
  onListChanged,
}: AreaTreeNodeProps) {
  const [expanded, setExpanded] = useState(false);
  const [children, setChildren] = useState<AreaNode[] | null>(null);
  const [loadingChildren, setLoadingChildren] = useState(false);
  const [hasChildren, setHasChildren] = useState(node.hasChildren);
  const [editModal, setEditModal] = useState(false);
  const [polygonModal, setPolygonModal] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  function loadChildren() {
    setLoadingChildren(true);
    areasApi
      .listAreaChildren(node.id, true)
      .then((items) => {
        setChildren(items);
        setHasChildren(items.length > 0);
      })
      .catch((err) => onError(errorMessage(err, 'No se pudieron cargar los subsectores')))
      .finally(() => setLoadingChildren(false));
  }

  function toggleExpand() {
    if (!expanded && children === null) {
      loadChildren();
    }
    setExpanded((e) => !e);
  }

  async function handleDelete() {
    try {
      await areasApi.deleteArea(node.id);
      onListChanged();
    } catch (err) {
      onError(errorMessage(err, 'No se pudo eliminar el sector'));
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
            <Button
              variant="ghost"
              small
              type="button"
              onClick={() =>
                onRequestCreate(node.id, () => {
                  loadChildren();
                  setExpanded(true);
                })
              }
            >
              + Subsector
            </Button>
          )}
          {canEdit && (
            <Button variant="ghost" small type="button" onClick={() => setPolygonModal(true)}>
              {node.hasPolygon ? 'Editar polígono' : 'Dibujar polígono'}
            </Button>
          )}
          {canEdit && (
            <Button variant="ghost" small type="button" onClick={() => setEditModal(true)}>
              Editar
            </Button>
          )}
          {canDelete && (
            <Button variant="ghost" small danger type="button" onClick={() => setConfirmingDelete(true)}>
              Eliminar
            </Button>
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
                parentAreaId={node.id}
                canCreate={canCreate}
                canEdit={canEdit}
                canDelete={canDelete}
                onError={onError}
                onRequestCreate={onRequestCreate}
                onListChanged={loadChildren}
              />
            ))}
        </ul>
      )}

      {editModal && (
        <AreaNameModal
          parentAreaId={parentAreaId}
          area={{ id: node.id, name: node.name }}
          onClose={() => setEditModal(false)}
          onSaved={() => {
            setEditModal(false);
            onListChanged();
          }}
        />
      )}

      {polygonModal && (
        <Suspense fallback={null}>
          <AreaPolygonModal
            areaId={node.id}
            areaName={node.name}
            parentAreaId={parentAreaId}
            onClose={() => setPolygonModal(false)}
            onSaved={() => {
              setPolygonModal(false);
              // Refreshes this level's nodes so the row's draw/edit-polygon label follows the save.
              onListChanged();
            }}
          />
        </Suspense>
      )}

      {confirmingDelete && (
        <ConfirmModal
          message={`¿Eliminar el sector "${node.name}" y todos sus subsectores?`}
          confirmLabel="Eliminar"
          danger
          onConfirm={handleDelete}
          onClose={() => setConfirmingDelete(false)}
        />
      )}
    </li>
  );
}

interface Props {
  canCreate: boolean;
  canEdit: boolean;
  canDelete: boolean;
}

export function AreaTree({ canCreate, canEdit, canDelete }: Props) {
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedAreaNodes>({ items: [], totalCount: 0, page: 1, pageSize: PAGE_SIZE });
  const [error, setError] = useState('');
  const [createModal, setCreateModal] = useState<CreateModalState | null>(null);

  function refreshRoots() {
    areasApi
      .listRootAreas(page, PAGE_SIZE, true)
      .then((r) => {
        const totalPages = Math.max(1, Math.ceil(r.totalCount / r.pageSize));
        if (page > totalPages) {
          setPage(totalPages);
          return;
        }
        setResult(r);
        setError('');
      })
      .catch((err) => setError(errorMessage(err, 'No se pudieron cargar los sectores')));
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
  }

  const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));

  return (
    <div className="area-tree-wrap">
      <div className="area-tree-header">
        {canCreate && (
          <Button type="button" onClick={() => openCreateModal(null, refreshRoots)}>
            Añadir sector principal
          </Button>
        )}
      </div>

      {error && <p className="error-text">{error}</p>}

      {result.items.length === 0 ? (
        <EmptyState>No hay sectores todavía</EmptyState>
      ) : (
        <ul className="area-tree">
          {result.items.map((node) => (
            <AreaTreeNode
              key={node.id}
              node={node}
              parentAreaId={null}
              canCreate={canCreate}
              canEdit={canEdit}
              canDelete={canDelete}
              onError={setError}
              onRequestCreate={openCreateModal}
              onListChanged={refreshRoots}
            />
          ))}
        </ul>
      )}

      <Pagination page={page} pageCount={totalPages} onPageChange={setPage} />

      {createModal && (
        <AreaNameModal parentAreaId={createModal.parentAreaId} onClose={() => setCreateModal(null)} onSaved={handleCreated} />
      )}
    </div>
  );
}
