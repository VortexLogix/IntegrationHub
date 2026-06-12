using IntegrationHub.MockEndpoints.Models;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationHub.MockEndpoints.Controllers;

[ApiController]
[Route("api/crm")]
public sealed class CrmController : ControllerBase
{
    [HttpPost("orders")]
    public ActionResult<OrderEventEnvelope> CreateOrderEvent([FromBody] CrmOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.ProductCode) || request.Quantity <= 0)
        {
            return BadRequest(new
            {
                error = "customerId, productCode, and quantity (> 0) are required."
            });
        }

        var envelope = new OrderEventEnvelope
        {
            EventId = $"evt-{Guid.NewGuid():N}",
            EventType = "OrderCreated",
            SourceSystem = "CRM",
            Payload = new
            {
                customerId = request.CustomerId,
                productCode = request.ProductCode,
                quantity = request.Quantity
            }
        };

        return Accepted(envelope);
    }
}
