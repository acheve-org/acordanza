using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;

namespace Acordanza.Core.Services;

// The "connection string locally, DefaultAzureCredential with a managed-identity client id
// in production" branch, factored out once it reached its sixth hand-written copy: Api and
// Worker each build it for the enrichment queue and the route-import queue, and Api builds
// it a fifth time for the Data Protection container. BlobStorageService (below in this same
// folder) already implements this branch correctly for a full BlobServiceClient plus the SAS
// signing neither caller here needs — this covers the two simpler shapes both processes
// construct straight off IConfiguration, so a new queue or container doesn't add a seventh.
public static class StorageClients
{
    private static readonly QueueClientOptions QueueOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };

    public static QueueClient QueueFor(IConfigurationSection storageSection, string queueName)
    {
        var connectionString = storageSection["ConnectionString"];
        if (!string.IsNullOrEmpty(connectionString))
        {
            return new QueueClient(connectionString, queueName, QueueOptions);
        }

        var accountName = RequireAccountName(storageSection);
        var credential = CredentialFor(storageSection);
        return new QueueClient(
            new Uri($"https://{accountName}.queue.core.windows.net/{queueName}"), credential, QueueOptions);
    }

    public static BlobContainerClient ContainerFor(IConfigurationSection storageSection, string containerName)
    {
        var connectionString = storageSection["ConnectionString"];
        if (!string.IsNullOrEmpty(connectionString))
        {
            return new BlobContainerClient(connectionString, containerName);
        }

        var accountName = RequireAccountName(storageSection);
        var credential = CredentialFor(storageSection);
        return new BlobContainerClient(
            new Uri($"https://{accountName}.blob.core.windows.net/{containerName}"), credential);
    }

    private static string RequireAccountName(IConfigurationSection storageSection) =>
        storageSection["AccountName"]
            ?? throw new InvalidOperationException("Missing Storage:AccountName configuration");

    private static DefaultAzureCredential CredentialFor(IConfigurationSection storageSection)
    {
        var managedIdentityClientId = storageSection["ManagedIdentityClientId"]
            ?? throw new InvalidOperationException("Missing Storage:ManagedIdentityClientId configuration");
        return new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { ManagedIdentityClientId = managedIdentityClientId });
    }
}
