using System.Diagnostics.CodeAnalysis;
using IntegrationHub.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace IntegrationHub.Functions.Functions;

[ExcludeFromCodeCoverage]
public sealed class StatusFunction(IStatusStore statusStore)
{
    [Function("StatusFunction")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "status/{correlationId}")] HttpRequest req,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return new BadRequestObjectResult(new
            {
                error = "correlationId is required."
            });
        }

        var sourceSystem = req.Query["sourceSystem"].FirstOrDefault();
        var entity = await statusStore.GetStatusAsync(correlationId, sourceSystem, cancellationToken).ConfigureAwait(false);

        if (entity is null)
        {
            return new NotFoundObjectResult(new
            {
                error = $"No status found for correlationId '{correlationId}'.",
                correlationId
            });
        }

        return new OkObjectResult(new
        {
            correlationId = entity.RowKey,
            status = entity.Status,
            message = entity.Message,
            sourceSystem = entity.SourceSystem,
            eventType = entity.EventType,
            lastUpdatedUtc = entity.LastUpdatedUtc
        });
    }
}
