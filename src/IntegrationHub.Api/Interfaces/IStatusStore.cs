namespace IntegrationHub.Api.Interfaces;

public sealed class OrderStatusEntity
{
    public string CorrelationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public DateTimeOffset LastUpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public interface IStatusStore
{
    Task<OrderStatusEntity?> GetStatusAsync(string correlationId, CancellationToken cancellationToken);
}
