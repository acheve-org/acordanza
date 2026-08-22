using Acordanza.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Acordanza.Api.Services;

// Validates an uploaded image (an HTTP concept — the controllers' two upload
// endpoints are its only callers); the allowlist/size limits it checks against
// live in Core's MediaUploadService since RouteEnrichmentService needs them
// too on its download path.
public static class ImageValidation
{
    // Matches Topo.FileName's HasMaxLength (AppDbContext) — the standard filesystem
    // filename ceiling. The client-supplied name is stored as-is (RoutesController.
    // UploadTopo, AreasController.UploadAreaMedia), so without this an oversized one
    // fails as an unhandled DbUpdateException instead of a clean 400 (SEC-7).
    public const int MaxFileNameLength = 255;

    public static ImageValidationError? ValidateImage(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return new ImageValidationError("la imagen es obligatoria");
        }
        if (!MediaUploadService.AllowedImageContentTypes.Contains(file.ContentType))
        {
            return new ImageValidationError("el formato de la imagen no es válido (usa JPEG, PNG o WebP)");
        }
        if (file.Length > MediaUploadService.MaxImageBytes)
        {
            return new ImageValidationError("la imagen es demasiado grande (máximo 15 MB)");
        }
        if (file.FileName.Length > MaxFileNameLength)
        {
            return new ImageValidationError("el nombre del archivo es demasiado largo");
        }

        return null;
    }
}
