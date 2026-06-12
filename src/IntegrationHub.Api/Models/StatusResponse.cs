namespace IntegrationHub.Api.Models;

public sealed class StatusResponse
{
    public string CorrelationId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset LastUpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Message { get; init; } = string.Empty;
}
