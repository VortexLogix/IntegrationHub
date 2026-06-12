using System.Text.Json.Nodes;

namespace IntegrationHub.Functions.Models;

public sealed class EnrichedOrder
{
    public string CorrelationId { get; init; } = string.Empty;

    public string EventId { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public string SourceSystem { get; init; } = string.Empty;

    public string ProductCode { get; init; } = string.Empty;

    public string ProductDescription { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal TotalAmount { get; init; }

    public string DeliveryPriority { get; init; } = string.Empty;

    public string RoutingTag { get; init; } = string.Empty;

    public bool IsClaimCheck { get; set; }

    public string? ClaimCheckBlobPath { get; set; }

    public JsonObject? OriginalPayload { get; set; }

    public DateTimeOffset ProcessedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
