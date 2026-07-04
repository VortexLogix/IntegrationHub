namespace IntegrationHub.Functions.Interfaces;

public interface IIdempotencyService
{
    Task<bool> IsDuplicateAsync(string eventId, CancellationToken cancellationToken);

    Task MarkProcessedAsync(string eventId, CancellationToken cancellationToken);
}
