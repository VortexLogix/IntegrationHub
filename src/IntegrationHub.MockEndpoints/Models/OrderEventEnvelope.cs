namespace IntegrationHub.MockEndpoints.Models;

public sealed class OrderEventEnvelope
{
    public string EventId { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public string SourceSystem { get; init; } = string.Empty;

    public object Payload { get; init; } = new { };
}
