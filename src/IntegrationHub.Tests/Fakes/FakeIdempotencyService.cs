using IntegrationHub.Functions.Interfaces;

namespace IntegrationHub.Tests.Fakes;

public sealed class FakeIdempotencyService : IIdempotencyService
{
    public HashSet<string> ProcessedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> IsDuplicateAsync(string eventId, CancellationToken cancellationToken)
    {
        return Task.FromResult(ProcessedKeys.Contains(eventId));
    }

    public Task MarkProcessedAsync(string eventId, CancellationToken cancellationToken)
    {
        ProcessedKeys.Add(eventId);
        return Task.CompletedTask;
    }
}
