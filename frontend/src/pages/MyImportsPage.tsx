import { useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Chip, EmptyState, List, ListRow } from 'acordanza-design-system';
import type { ChipStatus } from 'acordanza-design-system';
import * as topoImportsApi from '../api/topoImports';
import { queryKeys } from '../api/queryKeys';
import { errorMessage } from '../api/errorMessage';
import { routeDetailPath } from '../api/routes';
import { topoImportStatusLabels } from '../api/types';
import type { TopoImport, TopoImportStatus } from '../api/types';
import { AreaPicker } from '../components/AreaPicker';
import { formatDate } from '../format';

// The import pipeline's four states onto the Chip's three colours: a discard is the
// model's own answer and a failure is the pipeline breaking, but from the reader's side
// both are "this didn't produce a route", which is what the colour says.
const chipStatus: Record<TopoImportStatus, ChipStatus> = {
  pending: 'pending',
  completed: 'approved',
  discarded: 'rejected',
  failed: 'rejected',
};

// One page, not two: the list is the only place an upload's outcome can appear, so
// putting the form anywhere else would split one flow across two screens.
export function MyImportsPage() {
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [file, setFile] = useState<File | null>(null);
  const [areaId, setAreaId] = useState<number | null>(null);
  const [submitted, setSubmitted] = useState(false);

  const { data: imports, error } = useQuery({
    queryKey: queryKeys.myTopoImports(),
    queryFn: topoImportsApi.listMyTopoImports,
  });

  const uploadMutation = useMutation({
    mutationFn: () => topoImportsApi.createTopoImport(file!, areaId!),
    onSuccess: () => {
      setSubmitted(true);
      setFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
      queryClient.invalidateQueries({ queryKey: ['topo-imports'] });
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!file || !areaId) return;
    setSubmitted(false);
    uploadMutation.mutate();
  }

  // "Volver a intentarlo" re-uploads rather than re-enqueueing: the staged image is
  // deleted the moment a run discards it, and someone who disagrees with a discard
  // usually wants to try a better photo anyway. So this just sets the form up for them —
  // same sector, cursor in the file picker.
  function retry(item: TopoImport) {
    setAreaId(item.areaId);
    setSubmitted(false);
    fileInputRef.current?.focus();
    fileInputRef.current?.scrollIntoView({ block: 'center' });
  }

  return (
    <>
      <h1>Mis importaciones</h1>

      <section className="settings-section">
        <p>
          Sube la foto de un topo — una página de reseña, un panel del aparcamiento — y la leeremos para
          crear la vía. Elige el sector tú: es el único dato que no intentamos sacar de la foto.
        </p>
        <form onSubmit={handleSubmit}>
          {uploadMutation.error && (
            <p className="error-text">{errorMessage(uploadMutation.error, 'No se pudo subir el topo')}</p>
          )}
          <div className="grid-2">
            <label>
              Topo
              <input
                ref={fileInputRef}
                type="file"
                accept="image/jpeg,image/png,image/webp"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
            </label>
            <label>
              Sector
              <AreaPicker value={areaId} onChange={setAreaId} />
            </label>
          </div>
          <Button type="submit" disabled={!file || !areaId || uploadMutation.isPending}>
            Leer el topo
          </Button>
          {/* No polling and no progress bar: the work is asynchronous and there is nothing
              to stream. The row is already in the list below as «Leyendo el topo», and a
              reload updates it. */}
          {submitted && <p>Estamos leyendo el topo. El resultado aparecerá aquí en unos minutos.</p>}
        </form>
      </section>

      <section className="settings-section">
        {error ? (
          <p className="error-text">{errorMessage(error, 'No se pudieron cargar tus importaciones')}</p>
        ) : imports && imports.length > 0 ? (
          <List>
            {imports.map((item) => (
              <ListRow
                key={item.id}
                title={item.fileName}
                subtitle={<ImportSubtitle item={item} />}
                trailing={
                  <>
                    {item.status === 'discarded' && (
                      <Button variant="ghost" small type="button" onClick={() => retry(item)}>
                        Volver a intentarlo
                      </Button>
                    )}
                    <Chip status={chipStatus[item.status]}>{topoImportStatusLabels[item.status]}</Chip>
                  </>
                }
              />
            ))}
          </List>
        ) : (
          <EmptyState>Todavía no has subido ningún topo</EmptyState>
        )}
      </section>
    </>
  );
}

function ImportSubtitle({ item }: { item: TopoImport }) {
  const linkable = item.routeSlug !== null && item.routeAreaSlugPath !== null;

  return (
    <>
      {item.areaNamePath} · {formatDate(item.createdAt)}
      {item.outcome && <span className="user-row-meta">{item.outcome}</span>}
      {item.routeId !== null && (
        <span className="user-row-meta">
          {linkable ? (
            <Link to={routeDetailPath({ areaSlugPath: item.routeAreaSlugPath!, slug: item.routeSlug! })}>
              {item.routeName}
            </Link>
          ) : (
            item.routeName
          )}
        </span>
      )}
    </>
  );
}
