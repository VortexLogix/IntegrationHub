using IntegrationHub.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationHub.Api.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
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
    public ActionResult<StatusResponse> GetByCorrelationId(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return BadRequest(new
            {
                error = "correlationId is required."
            });
        }

        var response = new StatusResponse
        {
            CorrelationId = correlationId,
            Status = "Accepted",
            Message = "Event has been accepted for asynchronous processing.",
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };

        return Ok(response);
    }
}
