using System.Net.Http.Json;
using IntegrationHub.Functions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IntegrationHub.Functions.Services;

public sealed class OrderDeliveryService(
    IStatusStore statusStore,
    IIdempotencyService idempotencyService,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<OrderDeliveryService> logger) : IOrderDeliveryService
{
    public async Task<DeliveryResult> DeliverToErpAsync(EnrichedOrder order, string correlationId, CancellationToken cancellationToken)
    {
        var deliveryKey = $"delivery:{order.EventId}";

        if (await idempotencyService.IsDuplicateAsync(deliveryKey, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Delivery already completed for EventId: {EventId}. Recovering status.",
                order.EventId);
            await statusStore.SetStatusAsync(
                correlationId,
                order.SourceSystem,
                "Completed",
                "Order already delivered to ERP (recovered from partial failure).",
                order.EventType,
                cancellationToken).ConfigureAwait(false);
            return DeliveryResult.Accepted;
        }

        await statusStore.SetStatusAsync(
            correlationId,
            order.SourceSystem,
            "Processing",
            "Order is being processed and forwarded to ERP.",
            order.EventType,
            cancellationToken).ConfigureAwait(false);

        var erpEndpoint = configuration["ErpEndpointUrl"];
        if (string.IsNullOrWhiteSpace(erpEndpoint) || erpEndpoint.StartsWith("@Microsoft.KeyVault"))
        {
            logger.LogWarning("ErpEndpointUrl is not configured. Marking order as completed without ERP call.");
            await idempotencyService.MarkProcessedAsync(deliveryKey, cancellationToken).ConfigureAwait(false);
            await statusStore.SetStatusAsync(
                correlationId,
                order.SourceSystem,
                "Completed",
                "Order enriched. ERP endpoint not configured — manual processing required.",
                order.EventType,
                cancellationToken).ConfigureAwait(false);
            return DeliveryResult.NotConfigured;
        }

        using var httpClient = httpClientFactory.CreateClient("ErpClient");
        var erpPayload = new
        {
            correlationId = order.CorrelationId,
            eventId = order.EventId,
            productCode = order.ProductCode,
            quantity = order.Quantity,
            totalAmount = order.TotalAmount,
            deliveryPriority = order.DeliveryPriority
        };

        using var response = await httpClient.PostAsJsonAsync(erpEndpoint, erpPayload, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("ERP delivery accepted for CorrelationId: {CorrelationId}", correlationId);
            await idempotencyService.MarkProcessedAsync(deliveryKey, cancellationToken).ConfigureAwait(false);
            await statusStore.SetStatusAsync(
                correlationId,
                order.SourceSystem,
                "Completed",
                "Order enriched, forwarded to ERP, and delivery accepted.",
                order.EventType,
                cancellationToken).ConfigureAwait(false);
            return DeliveryResult.Accepted;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        logger.LogWarning(
            "ERP returned {StatusCode} for CorrelationId: {CorrelationId}. Body: {Body}",
            (int)response.StatusCode,
            correlationId,
            errorBody);

        await statusStore.SetStatusAsync(
            correlationId,
            order.SourceSystem,
            "Failed",
            $"ERP returned {(int)response.StatusCode}: {errorBody}",
            order.EventType,
            cancellationToken).ConfigureAwait(false);

        return DeliveryResult.Rejected;
    }
}
