namespace IntegrationHub.Functions.Interfaces;

public interface IClaimCheckStore
{
    Task<string> SavePayloadAsync(string correlationId, BinaryData payload, CancellationToken cancellationToken);
}
