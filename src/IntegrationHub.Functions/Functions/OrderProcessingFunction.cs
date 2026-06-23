using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using IntegrationHub.Functions.Models;
using IntegrationHub.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace IntegrationHub.Functions.Functions;

[ExcludeFromCodeCoverage]
public sealed class OrderProcessingFunction(
    IOrderDeliveryService deliveryService,
    ILogger<OrderProcessingFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("OrderProcessingFunction")]
    public async Task Run(
        [ServiceBusTrigger("orders", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var correlationId = message.CorrelationId 
            ?? (message.ApplicationProperties.TryGetValue("correlationId", out var cIdObj) ? cIdObj?.ToString() : null)
            ?? message.MessageId;
        var sourceSystem = message.ApplicationProperties.TryGetValue("sourceSystem", out var ss) ? ss?.ToString() : null;

        logger.LogInformation(
            "Processing order from queue. CorrelationId: {CorrelationId}, SourceSystem: {SourceSystem}",
            correlationId,
            sourceSystem);

        var body = message.Body.ToArray();
        var bodyString = Encoding.UTF8.GetString(body);

        EnrichedOrder? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize<EnrichedOrder>(bodyString, JsonOptions);
        }
        catch (JsonException)
        {
            try
            {
                var jsonBytes = Convert.FromBase64String(bodyString.Trim());
                bodyString = Encoding.UTF8.GetString(jsonBytes);
                deserialized = JsonSerializer.Deserialize<EnrichedOrder>(bodyString, JsonOptions);
            }
            catch (FormatException ex)
            {
                logger.LogError(ex, "Body is neither valid JSON nor valid Base64 for CorrelationId: {CorrelationId}", correlationId);
                throw;
            }
        }

        if (deserialized is null)
        {
            logger.LogError("Failed to deserialize enriched order for CorrelationId: {CorrelationId}", correlationId);
            throw new InvalidOperationException($"Failed to deserialize enriched order body for correlationId '{correlationId}'.");
        }

        if (!string.IsNullOrWhiteSpace(sourceSystem) && !string.Equals(sourceSystem, deserialized.SourceSystem, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "SourceSystem mismatch: message property '{MsgSource}' vs body '{BodySource}' for CorrelationId: {CorrelationId}",
                sourceSystem,
                deserialized.SourceSystem,
                correlationId);
        }

        await deliveryService.DeliverToErpAsync(deserialized, correlationId, cancellationToken).ConfigureAwait(false);
    }
}
