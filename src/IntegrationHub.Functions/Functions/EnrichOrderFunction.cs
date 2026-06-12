using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using IntegrationHub.Functions.Models;
using IntegrationHub.Functions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace IntegrationHub.Functions.Functions;

public sealed class EnrichOrderFunction
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IEnrichmentService _enrichmentService;
    private readonly ILogger<EnrichOrderFunction> _logger;

    public EnrichOrderFunction(IEnrichmentService enrichmentService, ILogger<EnrichOrderFunction> logger)
    {
        _enrichmentService = enrichmentService;
        _logger = logger;
    }

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
            var enriched = await _enrichmentService.EnrichAsync(orderEvent, correlationId, cancellationToken).ConfigureAwait(false);
            return new OkObjectResult(enriched);
        }
        catch (DuplicateEventException ex)
        {
            _logger.LogInformation(ex, "Duplicate event received: {EventId}", ex.EventId);
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
            _logger.LogError(ex, "Unhandled enrichment failure for correlationId {CorrelationId}", correlationId);
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
