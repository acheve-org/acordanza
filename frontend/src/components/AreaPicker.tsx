import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, ModalActions, TreeSelect } from 'acordanza-design-system';
import type { TreeSelectNode } from 'acordanza-design-system';
import * as areasApi from '../api/areas';
import { queryKeys } from '../api/queryKeys';
import type { AreaTreeNode } from '../api/types';
import { validationStatusLabels } from '../api/types';
import { Permissions } from '../api/permissions';
import { useAuth } from '../hooks/useAuth';

interface Props {
  value: number | null;
  onChange: (areaId: number | null) => void;
  allowCreate?: boolean;
  allowClear?: boolean;
}

// One global key, not one per call site — a crag picked on RouteFormPage is exactly the
// crag wanted first on MyTicksPage. Not cleared on logout: it's ergonomics, not PII.
const RECENTS_KEY = 'acordanza.recentAreas';
const MAX_HEAD_ROWS = 8;

function loadRecentAreaIds(): number[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(RECENTS_KEY) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter((x): x is number => typeof x === 'number') : [];
  } catch {
    return [];
  }
}

function persistRecentAreaIds(ids: number[]) {
  try {
    localStorage.setItem(RECENTS_KEY, JSON.stringify(ids));
  } catch {
    // Private-mode/quota failures are fine to swallow — recents are ergonomics, not required.
  }
}

function breadcrumb(node: AreaTreeNode, byId: Map<number, AreaTreeNode>): string {
  const parts: string[] = [];
  let current: AreaTreeNode | undefined = node;
  while (current) {
    parts.unshift(current.name);
    current = current.parentAreaId !== null ? byId.get(current.parentAreaId) : undefined;
  }
  return parts.join(' › ');
}

// The client has the whole tree, so it rolls subtree totals up itself in one pass rather
// than asking the backend to compute what a single client-side traversal gives for free.
function subtreeRouteCount(
  id: number,
  childrenOf: Map<number | null, AreaTreeNode[]>,
  byId: Map<number, AreaTreeNode>,
  cache: Map<number, number>,
): number {
  const cached = cache.get(id);
  if (cached !== undefined) return cached;
  let total = byId.get(id)?.routeCount ?? 0;
  for (const child of childrenOf.get(id) ?? []) {
    total += subtreeRouteCount(child.id, childrenOf, byId, cache);
  }
  cache.set(id, total);
  return total;
}

function descendantAreaCount(id: number, childrenOf: Map<number | null, AreaTreeNode[]>): number {
  const children = childrenOf.get(id) ?? [];
  return children.reduce((total, child) => total + 1 + descendantAreaCount(child.id, childrenOf), 0);
}

// A caller only ever sees their own pending/rejected areas here (the list is
// approved-or-mine, see AreasController), so the suffix marks "why is this one flagged?"
// rather than acting as a moderation cue.
function toTreeSelectNode(
  node: AreaTreeNode,
  label: string,
  subtreeCounts: Map<number, number>,
  dimEmpty: boolean,
): TreeSelectNode {
  const count = subtreeCounts.get(node.id) ?? 0;
  return {
    id: node.id,
    parentId: node.parentAreaId,
    label,
    suffix: node.validationStatus === 'approved' ? undefined : validationStatusLabels[node.validationStatus],
    meta: count > 0 ? String(count) : undefined,
    dimmed: dimEmpty && count === 0,
  };
}

export function AreaPicker({ value, onChange, allowCreate = false, allowClear = false }: Props) {
  const { hasPermission } = useAuth();
  const queryClient = useQueryClient();
  const canCreate = allowCreate && hasPermission(Permissions.areaCreate);

  const [hasOpened, setHasOpened] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [createParentId, setCreateParentId] = useState<number | null>(null);
  const [newName, setNewName] = useState('');
  const [recentIds, setRecentIds] = useState<number[]>(loadRecentAreaIds);

  // Deliberate exception to queryClient.ts's global staleTime: 0 — the area tree changes
  // rarely and all four AreaPicker call sites share this key, so whichever one opens first
  // across the session pays for the rest. Every area mutation (including this component's
  // own create, below) invalidates the ['areas'] prefix regardless. Fetching stays deferred
  // to first open (`enabled` flips on `onOpen`), plus the mount-time fetch when a value is
  // already selected, to resolve its label.
  const { data: nodes = [], isLoading } = useQuery({
    queryKey: queryKeys.areaTree(),
    queryFn: () => areasApi.listAreaTree(),
    enabled: hasOpened || value !== null,
    staleTime: 5 * 60_000,
  });

  const { byId, childrenOf } = useMemo(() => {
    const byId = new Map(nodes.map((n) => [n.id, n]));
    const childrenOf = new Map<number | null, AreaTreeNode[]>();
    for (const n of nodes) {
      const siblings = childrenOf.get(n.parentAreaId);
      if (siblings) siblings.push(n);
      else childrenOf.set(n.parentAreaId, [n]);
    }
    return { byId, childrenOf };
  }, [nodes]);

  const subtreeCounts = useMemo(() => {
    const cache = new Map<number, number>();
    for (const n of nodes) subtreeRouteCount(n.id, childrenOf, byId, cache);
    return cache;
  }, [nodes, childrenOf, byId]);

  const treeSelectNodes = useMemo<TreeSelectNode[]>(
    () => nodes.map((n) => toTreeSelectNode(n, n.name, subtreeCounts, allowClear)),
    [nodes, subtreeCounts, allowClear],
  );

  const headNodes = useMemo<TreeSelectNode[]>(() => {
    const seen = new Set<number>();
    const recentNodes: AreaTreeNode[] = [];
    for (const id of recentIds) {
      const node = byId.get(id);
      if (node && !seen.has(id)) {
        recentNodes.push(node);
        seen.add(id);
      }
    }

    // Padded with the highest-route-count roots when recents are thin, so a first-time
    // user gets a useful head list instead of an empty one.
    const paddingRoots = nodes
      .filter((n) => n.parentAreaId === null && !seen.has(n.id))
      .sort((a, b) => (subtreeCounts.get(b.id) ?? 0) - (subtreeCounts.get(a.id) ?? 0));

    return [...recentNodes, ...paddingRoots]
      .slice(0, MAX_HEAD_ROWS)
      .map((n) => toTreeSelectNode(n, breadcrumb(n, byId), subtreeCounts, allowClear));
  }, [nodes, byId, subtreeCounts, allowClear, recentIds]);

  const selected = value !== null ? byId.get(value) : undefined;

  // Only in filter mode, and only when the selected area has children — without it a user
  // filtering by a parent has no idea the backend is expanding to descendants.
  const subtreeHint =
    allowClear && selected && (childrenOf.get(selected.id)?.length ?? 0) > 0
      ? formatSubtreeHint(descendantAreaCount(selected.id, childrenOf))
      : null;

  function selectArea(id: number) {
    const next = [id, ...recentIds.filter((x) => x !== id)].slice(0, MAX_HEAD_ROWS);
    setRecentIds(next);
    persistRecentAreaIds(next);
    onChange(id);
  }

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    const created = await areasApi.createArea(name, createParentId);
    queryClient.invalidateQueries({ queryKey: ['areas'] });
    setNewName('');
    setShowCreate(false);
    selectArea(created.id);
  }

  return (
    <div className="picker-stack">
      <TreeSelect
        nodes={treeSelectNodes}
        value={value}
        onChange={selectArea}
        placeholder="Buscar sector…"
        closedValue={selected ? breadcrumb(selected, byId) : ''}
        headNodes={headNodes}
        headEmptyHint="Aún no hay sectores."
        leadingOption={allowClear ? { label: 'Cualquier sector', onSelect: () => onChange(null) } : undefined}
        onCreateChild={
          canCreate
            ? (parentId) => {
                setCreateParentId(parentId);
                setShowCreate(true);
              }
            : undefined
        }
        loading={isLoading}
        onOpen={() => setHasOpened(true)}
      />
      {subtreeHint && <p className="hint">{subtreeHint}</p>}
      {showCreate && (
        <div className="combo-create-form">
          <input
            type="text"
            placeholder="Nombre del nuevo sector"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
          />
          <ModalActions>
            <Button variant="ghost" small type="button" onClick={() => setShowCreate(false)}>
              Cancelar
            </Button>
            <Button type="button" onClick={handleCreate}>
              Añadir
            </Button>
          </ModalActions>
        </div>
      )}
    </div>
  );
}

function formatSubtreeHint(count: number): string {
  return `Incluye ${count} subsector${count === 1 ? '' : 'es'}`;
}
