using IntegrationHub.Api.Models;
using IntegrationHub.Functions.Services;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationHub.Api.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
    private readonly IStatusStore _statusStore;

    public StatusController(IStatusStore statusStore)
    {
        _statusStore = statusStore;
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            service = "IntegrationHub.Api",
            status = "Healthy",
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("{correlationId}")]
    public async Task<ActionResult<StatusResponse>> GetByCorrelationId(string correlationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return BadRequest(new
            {
                error = "correlationId is required."
            });
        }

        var entity = await _statusStore.GetStatusAsync(correlationId, null, cancellationToken).ConfigureAwait(false);

        if (entity is null)
        {
            return NotFound(new
            {
                error = $"No status found for correlationId '{correlationId}'. The event may not have been processed yet.",
                correlationId
            });
        }

        var response = new StatusResponse
        {
            CorrelationId = entity.RowKey,
            Status = entity.Status,
            Message = entity.Message,
            LastUpdatedUtc = entity.LastUpdatedUtc
        };

        return Ok(response);
    }
}
