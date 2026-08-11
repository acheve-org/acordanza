import { lazy, Suspense, useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, ConfirmModal, EmptyState, Pagination } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import { queryKeys } from '../api/queryKeys';
import type { AreaNode } from '../api/types';
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
  onRequestCreate: (parentAreaId: number, onCreated: () => void) => void;
  // Invalidates the node list at this level (this node's siblings) — needed after a delete
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
  onRequestCreate,
  onListChanged,
}: AreaTreeNodeProps) {
  const queryClient = useQueryClient();
  const [expanded, setExpanded] = useState(false);
  const [editModal, setEditModal] = useState(false);
  const [polygonModal, setPolygonModal] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  // Fetched once per node, on first expand — not refetched on collapse/re-expand
  // (`staleTime: Infinity`), only on an explicit invalidation from a mutation below it.
  const {
    data: children,
    isLoading: loadingChildren,
    error: childrenError,
  } = useQuery({
    queryKey: queryKeys.areaChildren(node.id),
    queryFn: () => areasApi.listAreaChildren(node.id, true),
    enabled: expanded,
    staleTime: Infinity,
  });

  const hasChildren = children ? children.length > 0 : node.hasChildren;

  function invalidateOwnChildren() {
    queryClient.invalidateQueries({ queryKey: queryKeys.areaChildren(node.id) });
  }

  const deleteMutation = useMutation({
    mutationFn: () => areasApi.deleteArea(node.id),
    onSuccess: onListChanged,
  });

  return (
    <li className="area-tree-node">
      <div className="area-tree-row">
        {hasChildren ? (
          <button
            type="button"
            className="area-tree-toggle"
            onClick={() => setExpanded((e) => !e)}
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
                  invalidateOwnChildren();
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
      {deleteMutation.isError && (
        <p className="error-text">{errorMessage(deleteMutation.error, 'No se pudo eliminar el sector')}</p>
      )}
      {expanded && (
        <ul className="area-tree-children">
          {loadingChildren && <li className="hint">Cargando…</li>}
          {childrenError && (
            <li className="hint error-text">
              {errorMessage(childrenError, 'No se pudieron cargar los subsectores')}
            </li>
          )}
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
                onRequestCreate={onRequestCreate}
                onListChanged={invalidateOwnChildren}
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
          onConfirm={() => deleteMutation.mutate()}
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
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [createModal, setCreateModal] = useState<CreateModalState | null>(null);

  const { data: result = { items: [], totalCount: 0, page: 1, pageSize: PAGE_SIZE }, error } = useQuery({
    queryKey: queryKeys.manageRootAreas(page),
    queryFn: () => areasApi.listRootAreas(page, PAGE_SIZE, true),
  });

  const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));

  // A mutation at root level (e.g. deleting the last item on the last page) can leave `page`
  // pointing past the new last page — settle back onto it rather than showing an empty page.
  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  function invalidateRoots() {
    queryClient.invalidateQueries({ queryKey: ['areas', 'roots', 'manage'] });
  }

  function openCreateModal(parentAreaId: number | null, onCreated: () => void) {
    setCreateModal({ parentAreaId, onCreated });
  }

  function handleCreated() {
    createModal?.onCreated();
    setCreateModal(null);
  }

  return (
    <div className="area-tree-wrap">
      <div className="area-tree-header">
        {canCreate && (
          <Button type="button" onClick={() => openCreateModal(null, invalidateRoots)}>
            Añadir sector principal
          </Button>
        )}
      </div>

      {error && <p className="error-text">{errorMessage(error, 'No se pudieron cargar los sectores')}</p>}

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
              onRequestCreate={openCreateModal}
              onListChanged={invalidateRoots}
            />
          ))}
        </ul>
      )}

      <Pagination page={page} totalCount={result.totalCount} pageSize={result.pageSize} onPageChange={setPage} />

      {createModal && (
        <AreaNameModal
          parentAreaId={createModal.parentAreaId}
          onClose={() => setCreateModal(null)}
          onSaved={handleCreated}
        />
      )}
    </div>
  );
}
