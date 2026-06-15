using IntegrationHub.Functions.Models;

namespace IntegrationHub.Functions.Services;

public enum DeliveryResult
{
    Accepted,
    Rejected,
    NotConfigured
}

public interface IOrderDeliveryService
{
    Task<DeliveryResult> DeliverToErpAsync(EnrichedOrder order, string correlationId, CancellationToken cancellationToken);
}
