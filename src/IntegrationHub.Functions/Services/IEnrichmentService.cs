using IntegrationHub.Functions.Models;

namespace IntegrationHub.Functions.Services;

public interface IEnrichmentService
{
    Task<EnrichedOrder> EnrichAsync(OrderEvent orderEvent, string correlationId, CancellationToken cancellationToken);
}
