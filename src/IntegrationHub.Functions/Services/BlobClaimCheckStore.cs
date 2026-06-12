using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace IntegrationHub.Functions.Services;

public sealed class BlobClaimCheckStore : IClaimCheckStore
{
    private readonly BlobContainerClient _containerClient;

    public BlobClaimCheckStore(BlobServiceClient blobServiceClient, IConfiguration configuration)
    {
        var containerName = configuration["ClaimCheckContainerName"];
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException("ClaimCheckContainerName setting is required.");
        }

        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task<string> SavePayloadAsync(string correlationId, BinaryData payload, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var blobName = $"{DateTime.UtcNow:yyyyMMdd}/{Uri.EscapeDataString(correlationId)}-{Guid.NewGuid():N}.json";
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(payload, overwrite: false, cancellationToken).ConfigureAwait(false);

        return blobName;
    }
}
