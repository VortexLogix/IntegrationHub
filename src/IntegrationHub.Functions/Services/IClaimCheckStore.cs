namespace IntegrationHub.Functions.Services;

public interface IClaimCheckStore
{
    Task<string> SavePayloadAsync(string correlationId, BinaryData payload, CancellationToken cancellationToken);
}
