using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntegrationHub.Functions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IntegrationHub.Functions.Services;

public sealed class EnrichmentService : IEnrichmentService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IIdempotencyService _idempotencyService;
    private readonly IClaimCheckStore _claimCheckStore;
    private readonly ILogger<EnrichmentService> _logger;
    private readonly int _claimCheckThresholdBytes;

    public EnrichmentService(
        IIdempotencyService idempotencyService,
        IClaimCheckStore claimCheckStore,
        IConfiguration configuration,
        ILogger<EnrichmentService> logger)
    {
        _idempotencyService = idempotencyService;
        _claimCheckStore = claimCheckStore;
        _logger = logger;

        _claimCheckThresholdBytes = configuration.GetValue<int?>("ClaimCheckThresholdBytes") ?? 65536;
    }

    public async Task<EnrichedOrder> EnrichAsync(OrderEvent orderEvent, string correlationId, CancellationToken cancellationToken)
    {
        ValidateOrderEvent(orderEvent);

        if (await _idempotencyService.IsDuplicateAsync(orderEvent.EventId, cancellationToken).ConfigureAwait(false))
        {
            throw new DuplicateEventException(orderEvent.EventId);
        }

        var productCode = GetRequiredPayloadValue(orderEvent.Payload, "productCode");
        var quantity = GetRequiredPayloadInt(orderEvent.Payload, "quantity");

        var (productDescription, unitPrice, routingTag) = GetProductDetails(productCode);
        var deliveryPriority = quantity >= 10 ? "high" : "standard";

        var enriched = new EnrichedOrder
        {
            CorrelationId = correlationId,
            EventId = orderEvent.EventId,
            EventType = orderEvent.EventType,
            SourceSystem = orderEvent.SourceSystem,
            ProductCode = productCode,
            ProductDescription = productDescription,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TotalAmount = unitPrice * quantity,
            DeliveryPriority = deliveryPriority,
            RoutingTag = routingTag,
            OriginalPayload = JsonNode.Parse(orderEvent.Payload.GetRawText())?.AsObject()
        };

        var serialized = JsonSerializer.Serialize(enriched, JsonSerializerOptions);
        var payloadBytes = Encoding.UTF8.GetByteCount(serialized);

        if (payloadBytes > _claimCheckThresholdBytes)
        {
            var blobPath = await _claimCheckStore
                .SavePayloadAsync(correlationId, BinaryData.FromString(serialized), cancellationToken)
                .ConfigureAwait(false);

            enriched.IsClaimCheck = true;
            enriched.ClaimCheckBlobPath = blobPath;
            enriched.OriginalPayload = null;

            _logger.LogInformation(
                "Claim-check applied for correlationId {CorrelationId}. Payload size: {PayloadBytes} bytes.",
                correlationId,
                payloadBytes);
        }

        await _idempotencyService.MarkProcessedAsync(orderEvent.EventId, cancellationToken).ConfigureAwait(false);
        return enriched;
    }

    private static void ValidateOrderEvent(OrderEvent orderEvent)
    {
        if (string.IsNullOrWhiteSpace(orderEvent.EventId))
        {
            throw new ValidationException("eventId is required.");
        }

        if (string.IsNullOrWhiteSpace(orderEvent.EventType))
        {
            throw new ValidationException("eventType is required.");
        }

        if (string.IsNullOrWhiteSpace(orderEvent.SourceSystem))
        {
            throw new ValidationException("sourceSystem is required.");
        }

        if (orderEvent.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ValidationException("payload is required.");
        }
    }

    private static string GetRequiredPayloadValue(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ValidationException($"payload.{propertyName} is required.");
        }

        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new ValidationException($"payload.{propertyName} is required.");
        }

        return result;
    }

    private static int GetRequiredPayloadInt(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new ValidationException($"payload.{propertyName} must be a valid number.");
        }

        if (result <= 0)
        {
            throw new ValidationException($"payload.{propertyName} must be greater than 0.");
        }

        return result;
    }

    private static (string Description, decimal UnitPrice, string RoutingTag) GetProductDetails(string productCode)
    {
        return productCode.ToUpperInvariant() switch
        {
            "SKU-9001" => ("Enterprise Sensor Kit", 1299.00m, "erp-priority"),
            "SKU-4100" => ("Warehouse Gateway", 799.00m, "erp-standard"),
            _ => ("General Merchandise", 199.00m, "erp-general")
        };
    }
}
