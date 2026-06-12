namespace IntegrationHub.MockEndpoints.Models;

public sealed class CrmOrderRequest
{
    public string CustomerId { get; init; } = string.Empty;

    public string ProductCode { get; init; } = string.Empty;

    public int Quantity { get; init; }
}
