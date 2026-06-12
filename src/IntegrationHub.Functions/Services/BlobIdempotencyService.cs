using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace IntegrationHub.Functions.Services;

public sealed class BlobIdempotencyService : IIdempotencyService
{
    private readonly BlobContainerClient _containerClient;

    public BlobIdempotencyService(BlobServiceClient blobServiceClient, IConfiguration configuration)
    {
        var containerName = configuration["IdempotencyContainerName"];
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException("IdempotencyContainerName setting is required.");
        }

        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task<bool> IsDuplicateAsync(string eventId, CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient(GetBlobName(eventId));
        var exists = await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false);
        return exists.Value;
    }

    public async Task MarkProcessedAsync(string eventId, CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var blobClient = _containerClient.GetBlobClient(GetBlobName(eventId));
        await blobClient.UploadAsync(BinaryData.FromString(string.Empty), overwrite: true, cancellationToken).ConfigureAwait(false);
    }

    private static string GetBlobName(string eventId)
    {
        return $"{Uri.EscapeDataString(eventId)}.processed";
    }
}
