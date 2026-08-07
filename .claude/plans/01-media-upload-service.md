# Extract a shared media-upload service

Status: planned (not started)
Depends on: nothing (AreaHierarchyService extraction already landed; upload code now uses `hierarchy.SlugPath(...)` for prefixes)

## Problem

`RoutesController.UploadTopo` and `AreasController.UploadAreaMedia` duplicate ~90% of their bodies:

- Image validation: allowed content types (`image/jpeg|png|webp`), 15 MB max, "la imagen es obligatoria" / format / size Spanish errors — constants duplicated per controller (`AllowedTopoContentTypes`/`AllowedMediaContentTypes`, `MaxTopoBytes`/`MaxMediaBytes`).
- Numbered blob naming: scan existing blob names under a prefix for the highest `-{NN}` suffix (`HighestToposNumber` and `HighestMediaNumber` are character-identical private helpers).
- The 5-attempt conditional-upload race loop (`overwrite: false`, catch `RequestFailedException` 412, try next number), ending in a Spanish `Conflict` error.
- Orphan-blob handling around `SaveChangesAsync`.

Known drift already present (fix as part of this): `UploadAreaMedia` deletes the just-uploaded blob if `SaveChangesAsync` throws (`catch (DbUpdateException)` → `blobStorage.DeleteAsync` → rethrow); **`UploadTopo` does not** — a failed row insert there leaves an orphaned blob occupying the name forever.

## Design

New `Services/MediaUploadService` (scoped or singleton — it only wraps `BlobStorageService`, which is a singleton; make it singleton) with roughly:

```csharp
public sealed record ImageValidationError(string Message);           // Spanish, ready for { error }
public ImageValidationError? ValidateImage(IFormFile? file);          // null = ok

// Scans usedBlobNames for the highest trailing number under prefix, then tries
// prefix{N:D2}{ext}, prefix{N+1:D2}{ext}, ... up to 5 conditional uploads.
// Returns the blob name that landed, or null if all attempts lost the race.
public Task<string?> UploadNumberedAsync(
    string blobPrefix, string extension, IReadOnlyList<string> usedBlobNames,
    Stream content, string contentType);

// Wraps a DB save so a failed row insert cleans up the just-uploaded blob.
public Task SaveOrCleanupAsync(string blobName, Func<Task> save);
```

Callers keep querying their own DbSet (`db.Topos` / `db.AreaMedia`) for `usedBlobNames` under the prefix — the service stays DB-agnostic. The `IFormFile` open/reopen per attempt matters: each retry must call `file.OpenReadStream()` fresh, so either pass the `IFormFile` itself or a `Func<Stream>` factory. Prefer `Func<Stream>` to keep ASP.NET types out of the service — actually `IFormFile` is fine in this codebase (no layering ambition); pick `IFormFile` for simplicity.

Move the shared constants (`AllowedImageContentTypes`, `MaxImageBytes = 15 MB`, `ImageUrlLifetime = 15 min`) into the service; note `[RequestSizeLimit]` attributes on the actions must keep a compile-time constant, so keep a `public const long MaxImageBytes` the attributes can reference.

## Steps

1. Create `Services/MediaUploadService.cs` with the API above; register in `Program.cs` next to `BlobStorageService`.
2. Rewrite `RoutesController.UploadTopo` on top of it — this is where the missing orphan-cleanup gets fixed (behavior change, intentional).
3. Rewrite `AreasController.UploadAreaMedia` on top of it; delete `HighestToposNumber`, `HighestMediaNumber`, and the per-controller constants.
4. Check `RoutesController.DeleteRoute` / `AreasController.DeleteArea` / `AreaMediaController.DeleteAreaMedia` blob-deletion paths — do NOT unify those here (delete ordering semantics differ deliberately: routes delete blobs before the row, areas after commit, each with an explanatory comment). Out of scope.
5. `dotnet build`; update `backend/CLAUDE.md` Services bullet.

## Verification

No test suite exists. Verify with the `run-acordanza` skill driver (it uploads nothing today, so manual check): upload a topo to a route and a media image to an area via the UI, upload a second one (numbering), delete one and upload again (number not reused), confirm blob names in Azurite (`docker compose` container) follow `{slug-path}/{slug}-NN.ext` and `{slug-path}/_media-NN.ext`.
