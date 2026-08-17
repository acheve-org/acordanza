import { useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button, Chip, ConfirmModal, EmptyState, List, ListRow } from 'acordanza-design-system';
import type { ChipStatus } from 'acordanza-design-system';
import * as routeImportsApi from '../api/routeImports';
import { queryKeys } from '../api/queryKeys';
import { errorMessage } from '../api/errorMessage';
import { routeDetailPath } from '../api/routes';
import { routeImportKindLabels, routeImportStatusLabels } from '../api/types';
import type { RouteImport, RouteImportKind, RouteImportStatus } from '../api/types';
import { AreaPicker } from '../components/AreaPicker';
import { formatDate } from '../format';

// The import pipeline's four states onto the Chip's three colours: a discard is the
// reader's own answer and a failure is the pipeline breaking, but from the reader's side
// both are "this didn't produce a route", which is what the colour says.
const chipStatus: Record<RouteImportStatus, ChipStatus> = {
  pending: 'pending',
  completed: 'approved',
  discarded: 'rejected',
  failed: 'rejected',
};

// One page and one list for both kinds, because the list is the only place either kind's
// outcome can appear — putting the two forms on separate screens would split one flow
// across two, and the sector picker is shared either way.
export function MyImportsPage() {
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const urlInputRef = useRef<HTMLInputElement>(null);

  const [kind, setKind] = useState<RouteImportKind>('image');
  const [file, setFile] = useState<File | null>(null);
  const [url, setUrl] = useState('');
  const [areaId, setAreaId] = useState<number | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const [deleting, setDeleting] = useState<RouteImport | null>(null);

  const { data: imports, error } = useQuery({
    queryKey: queryKeys.myRouteImports(),
    queryFn: routeImportsApi.listMyRouteImports,
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => routeImportsApi.deleteRouteImport(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['route-imports'] }),
  });

  const submitMutation = useMutation({
    mutationFn: () =>
      kind === 'image'
        ? routeImportsApi.createImageImport(file!, areaId!)
        : routeImportsApi.createUrlImport(url.trim(), areaId!),
    onSuccess: () => {
      setSubmitted(true);
      setFile(null);
      setUrl('');
      if (fileInputRef.current) fileInputRef.current.value = '';
      queryClient.invalidateQueries({ queryKey: ['route-imports'] });
    },
  });

  const ready = areaId !== null && (kind === 'image' ? file !== null : url.trim().length > 0);

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!ready) return;
    setSubmitted(false);
    submitMutation.mutate();
  }

  function switchTo(next: RouteImportKind) {
    setKind(next);
    setSubmitted(false);
    submitMutation.reset();
  }

  // "Volver a intentarlo" sets the form up again rather than re-enqueueing the row, and
  // the two kinds differ in what that's worth: a link genuinely can be re-read (the URL is
  // still on the row, and the user may want to fix it or the sector first), whereas a
  // photo's staged image is deleted the moment a run discards it, so that one is really a
  // re-upload. Same code path either way — one form, filled in.
  function retry(item: RouteImport) {
    setKind(item.kind);
    setAreaId(item.areaId);
    setSubmitted(false);
    submitMutation.reset();
    if (item.kind === 'url') {
      setUrl(item.sourceUrl ?? '');
      urlInputRef.current?.focus();
      urlInputRef.current?.scrollIntoView({ block: 'center' });
    } else {
      fileInputRef.current?.focus();
      fileInputRef.current?.scrollIntoView({ block: 'center' });
    }
  }

  return (
    <>
      <h1>Mis importaciones</h1>

      <section className="settings-section">
        <p>
          Crea una vía a partir de lo que ya tienes: la foto de un topo — una página de reseña, un panel
          del aparcamiento — o el enlace de una entrada que hable de la apertura. Elige el sector tú: es
          el único dato que no intentamos deducir.
        </p>

        <div className="segmented-control" role="group" aria-label="Tipo de importación">
          <Button
            type="button"
            variant={kind === 'image' ? 'primary' : 'ghost'}
            small
            aria-pressed={kind === 'image'}
            onClick={() => switchTo('image')}
          >
            Desde una foto
          </Button>
          <Button
            type="button"
            variant={kind === 'url' ? 'primary' : 'ghost'}
            small
            aria-pressed={kind === 'url'}
            onClick={() => switchTo('url')}
          >
            Desde un enlace
          </Button>
        </div>

        <form onSubmit={handleSubmit}>
          {submitMutation.error && (
            <p className="error-text">
              {errorMessage(submitMutation.error, 'No se pudo iniciar la importación')}
            </p>
          )}
          <div className="grid-2">
            {kind === 'image' ? (
              <label>
                Topo
                <input
                  ref={fileInputRef}
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                />
              </label>
            ) : (
              <label>
                Enlace
                <input
                  ref={urlInputRef}
                  type="url"
                  inputMode="url"
                  placeholder="https://..."
                  value={url}
                  onChange={(e) => setUrl(e.target.value)}
                />
              </label>
            )}
            <label>
              Sector
              <AreaPicker value={areaId} onChange={setAreaId} />
            </label>
          </div>
          <Button type="submit" disabled={!ready || submitMutation.isPending}>
            {kind === 'image' ? 'Leer el topo' : 'Leer la página'}
          </Button>
          {/* No polling and no progress bar: the work is asynchronous and there is nothing
              to stream. The row is already in the list below as «Leyendo», and a reload
              updates it. */}
          {submitted && (
            <p>
              {kind === 'image'
                ? 'Estamos leyendo el topo. El resultado aparecerá aquí en unos minutos.'
                : 'Estamos leyendo la página. El resultado aparecerá aquí en unos minutos.'}
            </p>
          )}
        </form>
      </section>

      <section className="settings-section">
        {error ? (
          <p className="error-text">{errorMessage(error, 'No se pudieron cargar tus importaciones')}</p>
        ) : null}
        {deleteMutation.isError && (
          <p className="error-text">{errorMessage(deleteMutation.error, 'No se pudo eliminar la importación')}</p>
        )}
        {imports && imports.length > 0 ? (
          <List>
            {imports.map((item) => (
              <ListRow
                key={item.id}
                title={item.kind === 'url' ? (item.sourceUrl ?? '') : (item.fileName ?? '')}
                subtitle={<ImportSubtitle item={item} />}
                trailing={
                  <>
                    {item.status === 'discarded' && (
                      <Button variant="ghost" small type="button" onClick={() => retry(item)}>
                        Volver a intentarlo
                      </Button>
                    )}
                    {/* Only the two states the backend will actually accept — a rejection
                        from here should mean a stale list, not a real user choice. */}
                    {(item.status === 'discarded' || item.status === 'failed') && (
                      <Button variant="ghost" small danger type="button" onClick={() => setDeleting(item)}>
                        Eliminar
                      </Button>
                    )}
                    <Chip status={chipStatus[item.status]}>{routeImportStatusLabels[item.status]}</Chip>
                  </>
                }
              />
            ))}
          </List>
        ) : !error ? (
          <EmptyState>Todavía no has importado ninguna vía</EmptyState>
        ) : null}
      </section>

      {deleting && (
        <ConfirmModal
          message="¿Eliminar esta importación?"
          confirmLabel="Eliminar"
          danger
          onConfirm={() => deleteMutation.mutate(deleting.id)}
          onClose={() => setDeleting(null)}
        />
      )}
    </>
  );
}

function ImportSubtitle({ item }: { item: RouteImport }) {
  const linkable = item.routeSlug !== null && item.routeAreaSlugPath !== null;

  return (
    <>
      {routeImportKindLabels[item.kind]} · {item.areaNamePath} · {formatDate(item.createdAt)}
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
