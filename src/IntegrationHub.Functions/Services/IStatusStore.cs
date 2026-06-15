namespace IntegrationHub.Functions.Services;

public sealed class OrderStatusEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Timestamp { get; set; }
}

public interface IStatusStore
{
    Task SetStatusAsync(string correlationId, string sourceSystem, string status, string message, string eventType, CancellationToken cancellationToken);
    Task<OrderStatusEntity?> GetStatusAsync(string correlationId, string? sourceSystem = null, CancellationToken cancellationToken = default);
}
