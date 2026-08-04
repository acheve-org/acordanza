using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Acordanza.Api.Services;

// Stores/serves topo images in Azure Blob Storage. In production the
// container app authenticates via its user-assigned managed identity (no
// account key ever exists); locally it talks to an Azurite emulator via a
// connection string, which is the only mode that carries an account key.
public class BlobStorageService
{
    private readonly BlobServiceClient _serviceClient;
    private readonly BlobContainerClient _container;
    private readonly bool _usesAccountKey;
    private readonly Uri? _publicBlobEndpoint;

    public BlobStorageService(IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage");
        var containerName = section["ContainerName"]
            ?? throw new InvalidOperationException("Missing Storage:ContainerName configuration");
        var connectionString = section["ConnectionString"];

        // Only set for the dockerized dev backend (docker-compose.override.yml):
        // it reaches Azurite via the "azurite" compose hostname, but a SAS URL
        // handed to the browser must instead point at the host-mapped
        // "localhost" address, since the browser runs outside the compose
        // network. Unset everywhere else (plain `dotnet run` locally, and
        // production, where the internal and externally-reachable hostnames
        // are the same real storage account FQDN).
        var publicBlobEndpoint = section["PublicBlobEndpoint"];
        _publicBlobEndpoint = string.IsNullOrEmpty(publicBlobEndpoint) ? null : new Uri(publicBlobEndpoint);

        BlobServiceClient serviceClient;
        if (!string.IsNullOrEmpty(connectionString))
        {
            serviceClient = new BlobServiceClient(connectionString);
            _usesAccountKey = true;
        }
        else
        {
            var accountName = section["AccountName"]
                ?? throw new InvalidOperationException("Missing Storage:AccountName configuration");
            var managedIdentityClientId = section["ManagedIdentityClientId"]
                ?? throw new InvalidOperationException("Missing Storage:ManagedIdentityClientId configuration");

            var credential = new DefaultAzureCredential(
                new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
            serviceClient = new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"), credential);
            _usesAccountKey = false;
        }

        _serviceClient = serviceClient;
        _container = serviceClient.GetBlobContainerClient(containerName);
    }

    public async Task EnsureContainerExistsAsync()
    {
        // Only relevant locally (Azurite) — in production the container is
        // provisioned by Terraform, and the managed identity's RBAC role
        // doesn't include creating containers.
        if (_usesAccountKey)
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.None);
        }
    }

    // overwrite: false fails with a 412 (via RequestFailedException) instead of silently
    // replacing an existing blob at that name — used by RoutesController.UploadTopo to
    // detect a name collision (e.g. a racing upload) and retry under a different name
    // rather than clobbering someone else's topo image.
    public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite = true)
    {
        var blob = _container.GetBlobClient(blobName);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = overwrite ? null : new BlobRequestConditions { IfNoneMatch = ETag.All }
        });
    }

    public async Task DeleteAsync(string blobName)
    {
        await _container.GetBlobClient(blobName).DeleteIfExistsAsync();
    }

    public async Task<string> GetReadSasUrlAsync(string blobName, TimeSpan validFor)
    {
        var blob = _container.GetBlobClient(blobName);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(validFor)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        Uri sasUri;
        if (_usesAccountKey)
        {
            sasUri = blob.GenerateSasUri(sasBuilder);
        }
        else
        {
            // No account key exists in production — the SAS is instead signed
            // with a short-lived user delegation key obtained via the managed
            // identity's Azure AD token.
            var delegationKey = await _serviceClient.GetUserDelegationKeyAsync(
                new BlobGetUserDelegationKeyOptions(sasBuilder.ExpiresOn));
            var sasQuery = sasBuilder.ToSasQueryParameters(delegationKey.Value, _container.AccountName);
            sasUri = new UriBuilder(blob.Uri) { Query = sasQuery.ToString() }.Uri;
        }

        if (_publicBlobEndpoint is not null)
        {
            sasUri = new UriBuilder(sasUri)
            {
                Scheme = _publicBlobEndpoint.Scheme,
                Host = _publicBlobEndpoint.Host,
                Port = _publicBlobEndpoint.Port
            }.Uri;
        }

        return sasUri.ToString();
    }
}
