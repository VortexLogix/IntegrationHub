using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using IntegrationHub.Functions.Models;
using IntegrationHub.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace IntegrationHub.Functions.Functions;

[ExcludeFromCodeCoverage]
public sealed class EnrichOrderFunction(
    IEnrichmentService enrichmentService,
    ILogger<EnrichOrderFunction> logger)
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    [Function("EnrichOrderFunction")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "events/enrich")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        OrderEvent? orderEvent;
        try
        {
            orderEvent = await JsonSerializer.DeserializeAsync<OrderEvent>(req.Body, JsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new
            {
                error = "Request payload is not valid JSON."
            });
        }

        if (orderEvent is null)
        {
            return new BadRequestObjectResult(new
            {
                error = "Request payload is required."
            });
        }

        var correlationId = req.Headers["x-correlation-id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = string.IsNullOrWhiteSpace(orderEvent.EventId) ? Guid.NewGuid().ToString("N") : orderEvent.EventId;
        }

        try
        {
            var enriched = await enrichmentService.EnrichAsync(orderEvent, correlationId, cancellationToken).ConfigureAwait(false);
            return new OkObjectResult(enriched);
        }
        catch (DuplicateEventException ex)
        {
            logger.LogInformation(ex, "Duplicate event received: {EventId}", ex.EventId);
            return new AcceptedResult();
        }
        catch (ValidationException ex)
        {
            return new BadRequestObjectResult(new
            {
                error = ex.Message,
                correlationId
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled enrichment failure for correlationId {CorrelationId}", correlationId);
            return new ObjectResult(new
            {
                error = "Unexpected error while enriching order.",
                correlationId
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
