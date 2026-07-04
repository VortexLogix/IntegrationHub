using System.Diagnostics.CodeAnalysis;
using IntegrationHub.Functions.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace IntegrationHub.Functions.Functions;

[ExcludeFromCodeCoverage]
public sealed class StatusFunction(IStatusStore statusStore)
{
    [Function("StatusFunction")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "status/{correlationId}")] HttpRequest req,
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
    [Function("RecentStatusFunction")]
    public async Task<IActionResult> RunRecent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "status/recent")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        var limitStr = req.Query["limit"].FirstOrDefault();
        int limit = 50;
        if (int.TryParse(limitStr, out int parsed) && parsed > 0)
        {
            limit = Math.Min(parsed, 100);
        }

        var entities = await statusStore.GetRecentOrdersAsync(limit, cancellationToken).ConfigureAwait(false);

        return new OkObjectResult(entities.Select(entity => new
        {
            correlationId = entity.RowKey,
            status = entity.Status,
            message = entity.Message,
            sourceSystem = entity.SourceSystem,
            eventType = entity.EventType,
            lastUpdatedUtc = entity.LastUpdatedUtc
        }));
    }
}
