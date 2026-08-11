using Azure;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Core.Services;

public sealed record ImageValidationError(string Message);

// Shared upload logic for the two "numbered image under a slug prefix" flows
// (RoutesController.UploadTopo, AreasController.UploadAreaMedia) plus the
// enrichment worker's topo import: content-type/size allowlist, the
// numbered-blob-name race loop, and DB-save-vs-blob-orphan cleanup.
// DB-agnostic — callers still query their own DbSet for usedBlobNames.
public class MediaUploadService(BlobStorageService blobStorage)
{
    public static readonly HashSet<string> AllowedImageContentTypes = ["image/jpeg", "image/png", "image/webp"];
    public const long MaxImageBytes = 15 * 1024 * 1024;
    public static readonly TimeSpan ImageUrlLifetime = TimeSpan.FromMinutes(15);

    // Names are "{prefix}{NN}{extension}" — the next number is the highest one actually
    // in use under this prefix, not just a plain count, so a deleted image's number
    // never gets reissued to a surviving one with a higher number.
    //
    // A concurrent upload could grab the same number between the caller's usedBlobNames
    // query and the write below — upload conditionally (fails instead of overwriting if
    // the name is already taken) and retry under the next number when that happens.
    // Returns the blob name that landed, or null if all attempts lost the race.
    public async Task<string?> UploadNumberedAsync(
        string blobPrefix, string extension, IReadOnlyList<string> usedBlobNames, Stream content, string contentType)
    {
        var nextNumber = HighestNumber(usedBlobNames, blobPrefix) + 1;

        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = $"{blobPrefix}{nextNumber + attempt:D2}{extension}";
            try
            {
                if (content.CanSeek) content.Position = 0;
                await blobStorage.UploadAsync(candidate, content, contentType, overwrite: false);
                return candidate;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Name taken by a racing upload — try the next number.
            }
        }

        return null;
    }

    // Wraps a DB save so a failed row insert cleans up the just-uploaded blob rather
    // than leaving it orphaned, occupying its name forever.
    public async Task SaveOrCleanupAsync(string blobName, Func<Task> save)
    {
        try
        {
            await save();
        }
        catch (DbUpdateException)
        {
            await blobStorage.DeleteAsync(blobName);
            throw;
        }
    }

    // Parses the trailing "-{number}" out of each blob name (after the shared prefix,
    // before the extension) and returns the highest one found, or 0 if none/none parse.
    private static int HighestNumber(IReadOnlyList<string> blobNames, string prefix)
    {
        var highest = 0;
        foreach (var blobName in blobNames)
        {
            var numberPart = Path.GetFileNameWithoutExtension(blobName[prefix.Length..]);
            if (int.TryParse(numberPart, out var number) && number > highest)
            {
                highest = number;
            }
        }
        return highest;
    }
}
