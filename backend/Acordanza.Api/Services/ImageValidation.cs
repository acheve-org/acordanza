using Acordanza.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Acordanza.Api.Services;

// Validates an uploaded image (an HTTP concept — the controllers' two upload
// endpoints are its only callers); the allowlist/size limits it checks against
// live in Core's MediaUploadService since RouteEnrichmentService needs them
// too on its download path.
public static class ImageValidation
{
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

        return null;
    }
}
