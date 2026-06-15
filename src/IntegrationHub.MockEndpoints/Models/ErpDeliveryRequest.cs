namespace IntegrationHub.MockEndpoints.Models;

public sealed class ErpDeliveryRequest
{
    public string CorrelationId { get; init; } = string.Empty;

    public string EventId { get; init; } = string.Empty;

    public string ProductCode { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal TotalAmount { get; init; }

    public string DeliveryPriority { get; init; } = string.Empty;
}
