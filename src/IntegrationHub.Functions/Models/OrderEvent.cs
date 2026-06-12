using System.Text.Json;

namespace IntegrationHub.Functions.Models;

public sealed class OrderEvent
{
    public string EventId { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public string SourceSystem { get; init; } = string.Empty;

    public JsonElement Payload { get; init; }
}
