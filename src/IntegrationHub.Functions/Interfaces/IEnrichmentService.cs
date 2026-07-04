using IntegrationHub.Functions.Models;

namespace IntegrationHub.Functions.Interfaces;

public interface IEnrichmentService
{
    Task<EnrichedOrder> EnrichAsync(OrderEvent orderEvent, string correlationId, CancellationToken cancellationToken);
}
