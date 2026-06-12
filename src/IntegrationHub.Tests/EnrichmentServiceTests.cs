using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using IntegrationHub.Functions.Models;
using IntegrationHub.Functions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntegrationHub.Tests;

public sealed class EnrichmentServiceTests
{
    // ── Happy-path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_ValidEvent_ReturnsEnrichedOrder()
    {
        var idempotency = new InMemoryIdempotencyService();
        var claimStore = new FakeClaimCheckStore();
        var service = CreateService(idempotency, claimStore, claimCheckThresholdBytes: 65536);

        var orderEvent = CreateOrderEvent("evt-001", "SKU-9001", 2);

        var result = await service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None);

        Assert.Equal("evt-001", result.CorrelationId);
        Assert.Equal("Enterprise Sensor Kit", result.ProductDescription);
        Assert.Equal(1299.00m, result.UnitPrice);
        Assert.Equal(2598.00m, result.TotalAmount);
        Assert.False(result.IsClaimCheck);
    }

    [Fact]
    public async Task EnrichAsync_Sku4100_ReturnsWarehouseGateway()
    {
        var service = CreateService(new InMemoryIdempotencyService(), new FakeClaimCheckStore(), 65536);
        var orderEvent = CreateOrderEvent("evt-sku4100", "SKU-4100", 1);

        var result = await service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None);

        Assert.Equal("Warehouse Gateway", result.ProductDescription);
        Assert.Equal(799.00m, result.UnitPrice);
        Assert.Equal("erp-standard", result.RoutingTag);
    }

    [Fact]
    public async Task EnrichAsync_UnknownSku_ReturnsGeneralMerchandise()
    {
        var service = CreateService(new InMemoryIdempotencyService(), new FakeClaimCheckStore(), 65536);
        var orderEvent = CreateOrderEvent("evt-unknown", "SKU-XXXX", 1);

        var result = await service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None);

        Assert.Equal("General Merchandise", result.ProductDescription);
        Assert.Equal(199.00m, result.UnitPrice);
        Assert.Equal("erp-general", result.RoutingTag);
    }

    // ── Delivery priority ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(10, "high")]
    [InlineData(50, "high")]
    [InlineData(9,  "standard")]
    [InlineData(1,  "standard")]
    public async Task EnrichAsync_DeliveryPriority_BasedOnQuantity(int quantity, string expectedPriority)
    {
        var service = CreateService(new InMemoryIdempotencyService(), new FakeClaimCheckStore(), 65536);
        var orderEvent = CreateOrderEvent($"evt-qty-{quantity}", "SKU-9001", quantity);

        var result = await service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None);

        Assert.Equal(expectedPriority, result.DeliveryPriority);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_DuplicateEvent_ThrowsDuplicateEventException()
    {
        var idempotency = new InMemoryIdempotencyService { Duplicate = true };
        var service = CreateService(idempotency, new FakeClaimCheckStore(), 65536);
        var orderEvent = CreateOrderEvent("evt-dup", "SKU-9001", 1);

        await Assert.ThrowsAsync<DuplicateEventException>(() =>
            service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None));
    }

    [Fact]
    public async Task EnrichAsync_SecondCallSameEvent_MarkedAsProcessed()
    {
        var idempotency = new InMemoryIdempotencyService();
        var service = CreateService(idempotency, new FakeClaimCheckStore(), 65536);
        var orderEvent = CreateOrderEvent("evt-mark", "SKU-9001", 1);

        // First call — should succeed and mark event as processed.
        await service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None);

        // Simulate second delivery with duplicate flag now set.
        idempotency.Duplicate = true;
        await Assert.ThrowsAsync<DuplicateEventException>(() =>
            service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None));
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_MissingProductCode_ThrowsValidationException()
    {
        var service = CreateService(new InMemoryIdempotencyService(), new FakeClaimCheckStore(), 65536);
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"quantity\":2}");
        var orderEvent = new OrderEvent { EventId = "evt-002", EventType = "OrderCreated", SourceSystem = "CRM", Payload = payload };

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None));
    }

    [Fact]
    public async Task EnrichAsync_ZeroQuantity_ThrowsValidationException()
    {
        var service = CreateService(new InMemoryIdempotencyService(), new FakeClaimCheckStore(), 65536);
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"productCode\":\"SKU-9001\",\"quantity\":0}");
        var orderEvent = new OrderEvent { EventId = "evt-qty0", EventType = "OrderCreated", SourceSystem = "CRM", Payload = payload };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None));
        Assert.Contains("quantity", ex.Message);
    }

    [Fact]
    public async Task EnrichAsync_NegativeQuantity_ThrowsValidationException()
    {
        var service = CreateService(new InMemoryIdempotencyService(), new FakeClaimCheckStore(), 65536);
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"productCode\":\"SKU-9001\",\"quantity\":-5}");
        var orderEvent = new OrderEvent { EventId = "evt-neg", EventType = "OrderCreated", SourceSystem = "CRM", Payload = payload };

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"eventId\":\"\",\"eventType\":\"OrderCreated\",\"sourceSystem\":\"CRM\"}", "eventId")]
    [InlineData("{\"eventId\":\"e1\",\"eventType\":\"\",\"sourceSystem\":\"CRM\"}",   "eventType")]
    [InlineData("{\"eventId\":\"e1\",\"eventType\":\"OrderCreated\",\"sourceSystem\":\"\"}", "sourceSystem")]
    public async Task EnrichAsync_MissingRequiredTopLevelField_ThrowsValidationException(string json, string expectedField)
    {
        var service = CreateService(new InMemoryIdempotencyService(), new FakeClaimCheckStore(), 65536);
        var dto = JsonSerializer.Deserialize<OrderEvent>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"productCode\":\"SKU-9001\",\"quantity\":1}");
        var orderEvent = new OrderEvent
        {
            EventId = dto!.EventId,
            EventType = dto.EventType,
            SourceSystem = dto.SourceSystem,
            Payload = payload
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None));
        Assert.Contains(expectedField, ex.Message);
    }

    // ── Claim-check ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_LargePayload_UsesClaimCheck()
    {
        var idempotency = new InMemoryIdempotencyService();
        var claimStore = new FakeClaimCheckStore();
        var service = CreateService(idempotency, claimStore, claimCheckThresholdBytes: 64);

        var payload = JsonSerializer.Deserialize<JsonElement>(
            "{\"productCode\":\"SKU-4100\",\"quantity\":5,\"notes\":\"" + new string('x', 500) + "\"}");

        var orderEvent = new OrderEvent
        {
            EventId = "evt-003",
            EventType = "OrderCreated",
            SourceSystem = "CRM",
            Payload = payload
        };

        var result = await service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None);

        Assert.True(result.IsClaimCheck);
        Assert.False(string.IsNullOrWhiteSpace(result.ClaimCheckBlobPath));
        Assert.Null(result.OriginalPayload);
        Assert.Equal(1, claimStore.SaveCalls);
    }

    [Fact]
    public async Task EnrichAsync_SmallPayload_DoesNotUseClaimCheck()
    {
        var claimStore = new FakeClaimCheckStore();
        var service = CreateService(new InMemoryIdempotencyService(), claimStore, claimCheckThresholdBytes: 65536);
        var orderEvent = CreateOrderEvent("evt-small", "SKU-9001", 1);

        var result = await service.EnrichAsync(orderEvent, orderEvent.EventId, CancellationToken.None);

        Assert.False(result.IsClaimCheck);
        Assert.Equal(0, claimStore.SaveCalls);
    }

    // ── Notification service ──────────────────────────────────────────────────

    [Fact]
    public async Task NotificationService_NoWebhookConfigured_DoesNotThrow()
    {
        var service = new HttpNotificationService(
            new HttpClient(),
            new ConfigurationBuilder().Build(),   // empty config — no webhook URL
            NullLogger<HttpNotificationService>.Instance);

        // Should silently swallow the missing config, not throw.
        await service.SendDeadLetterAlertAsync("msg-1", "corr-1", "Expired", null, CancellationToken.None);
    }

    [Fact]
    public async Task NotificationService_SendFailureAlert_DoesNotThrow()
    {
        var service = new HttpNotificationService(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<HttpNotificationService>.Instance);

        await service.SendFailureAlertAsync("EnrichmentFailed", "corr-2", "Unexpected error", CancellationToken.None);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EnrichmentService CreateService(
        IIdempotencyService idempotencyService,
        IClaimCheckStore claimCheckStore,
        int claimCheckThresholdBytes)
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ClaimCheckThresholdBytes"] = claimCheckThresholdBytes.ToString()
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new EnrichmentService(
            idempotencyService,
            claimCheckStore,
            configuration,
            NullLogger<EnrichmentService>.Instance);
    }

    private static OrderEvent CreateOrderEvent(string eventId, string productCode, int quantity)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            $"{{\"productCode\":\"{productCode}\",\"quantity\":{quantity}}}");

        return new OrderEvent
        {
            EventId = eventId,
            EventType = "OrderCreated",
            SourceSystem = "CRM",
            Payload = payload
        };
    }

    private sealed class InMemoryIdempotencyService : IIdempotencyService
    {
        public bool Duplicate { get; set; }

        public Task<bool> IsDuplicateAsync(string eventId, CancellationToken cancellationToken)
            => Task.FromResult(Duplicate);

        public Task MarkProcessedAsync(string eventId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeClaimCheckStore : IClaimCheckStore
    {
        public int SaveCalls { get; private set; }

        public Task<string> SavePayloadAsync(string correlationId, BinaryData payload, CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.FromResult($"claims/{correlationId}.json");
        }
    }
}
