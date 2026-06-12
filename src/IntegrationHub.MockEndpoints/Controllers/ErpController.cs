using IntegrationHub.MockEndpoints.Models;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationHub.MockEndpoints.Controllers;

[ApiController]
[Route("api/erp")]
public sealed class ErpController : ControllerBase
{
    [HttpPost("deliveries")]
    public IActionResult ReceiveDelivery([FromBody] ErpDeliveryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationId) || string.IsNullOrWhiteSpace(request.EventId))
        {
            return BadRequest(new
            {
                error = "correlationId and eventId are required."
            });
        }

        return Ok(new
        {
            status = "Received",
            request.CorrelationId,
            request.EventId,
            receivedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
