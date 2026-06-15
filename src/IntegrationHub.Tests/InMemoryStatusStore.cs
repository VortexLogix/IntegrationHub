using IntegrationHub.Functions.Services;

namespace IntegrationHub.Tests;

public sealed class InMemoryStatusStore : IStatusStore
{
    public List<OrderStatusEntity> AllWrites { get; } = [];
    public OrderStatusEntity? Latest(string correlationId) =>
        AllWrites.LastOrDefault(e => e.RowKey.Equals(correlationId, StringComparison.OrdinalIgnoreCase));
    public OrderStatusEntity? Latest(string correlationId, string sourceSystem) =>
        AllWrites.LastOrDefault(e =>
            e.RowKey.Equals(correlationId, StringComparison.OrdinalIgnoreCase) &&
            e.PartitionKey.Equals(sourceSystem, StringComparison.OrdinalIgnoreCase));

    public Task SetStatusAsync(
        string correlationId,
        string sourceSystem,
        string status,
        string message,
        string eventType,
        CancellationToken cancellationToken)
    {
        var entity = new OrderStatusEntity
        {
            PartitionKey = sourceSystem,
            RowKey = correlationId,
            Status = status,
            Message = message,
            SourceSystem = sourceSystem,
            EventType = eventType,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            Timestamp = DateTimeOffset.UtcNow
        };
        AllWrites.Add(entity);
        return Task.CompletedTask;
    }

    public Task<OrderStatusEntity?> GetStatusAsync(string correlationId, string? sourceSystem = null, CancellationToken cancellationToken = default)
    {
        var entity = sourceSystem is not null
            ? Latest(correlationId, sourceSystem)
            : Latest(correlationId);
        return Task.FromResult(entity);
    }
}
