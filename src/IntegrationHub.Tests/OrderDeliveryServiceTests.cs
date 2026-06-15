using System.Net;
using IntegrationHub.Functions.Models;
using IntegrationHub.Functions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IntegrationHub.Tests;

public sealed class OrderDeliveryServiceTests
{
    private static readonly EnrichedOrder SampleOrder = new()
    {
        CorrelationId = "test-001",
        EventId = "evt-test-001",
        SourceSystem = "CRM",
        EventType = "OrderCreated",
        ProductCode = "SKU-9001",
        Quantity = 3,
        TotalAmount = 3897.00m,
        DeliveryPriority = "standard"
    };

    [Fact]
    public async Task DeliverToErpAsync_NoEndpointConfigured_ReturnsNotConfigured()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder().Build();
        var factory = new FakeHttpClientFactory();
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);

        var result = await service.DeliverToErpAsync(SampleOrder, "test-001", CancellationToken.None);

        Assert.Equal(DeliveryResult.NotConfigured, result);
        var entity = store.Latest("test-001");
        Assert.NotNull(entity);
        Assert.Equal("Completed", entity.Status);
    }

    [Fact]
    public async Task DeliverToErpAsync_ErpReturnsSuccess_SetsCompleted()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);

        var result = await service.DeliverToErpAsync(SampleOrder, "test-001", CancellationToken.None);

        Assert.Equal(DeliveryResult.Accepted, result);
        var entity = store.Latest("test-001");
        Assert.NotNull(entity);
        Assert.Equal("Completed", entity.Status);
    }

    [Fact]
    public async Task DeliverToErpAsync_ErpReturnsError_SetsFailed()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory(HttpStatusCode.InternalServerError);
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);

        var result = await service.DeliverToErpAsync(SampleOrder, "test-001", CancellationToken.None);

        Assert.Equal(DeliveryResult.Rejected, result);
        var entity = store.Latest("test-001");
        Assert.NotNull(entity);
        Assert.Equal("Failed", entity.Status);
        Assert.Contains("500", entity.Message);
    }

    [Fact]
    public async Task DeliverToErpAsync_SetsProcessingBeforeErpCall()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);

        await service.DeliverToErpAsync(SampleOrder, "test-001", CancellationToken.None);

        var statuses = store.AllWrites
            .Where(e => e.RowKey == "test-001")
            .Select(e => e.Status)
            .ToList();

        Assert.Contains("Processing", statuses);
    }

    [Fact]
    public async Task DeliverToErpAsync_IdempotentDelivery_SkipsErpAndRecoversStatus()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var idempotency = new FakeIdempotencyService();
        await idempotency.MarkProcessedAsync($"delivery:{SampleOrder.EventId}", CancellationToken.None);
        var service = CreateService(store, config, factory, idempotency);

        var callsBefore = factory.CallCount;
        var result = await service.DeliverToErpAsync(SampleOrder, "test-001", CancellationToken.None);

        Assert.Equal(DeliveryResult.Accepted, result);
        Assert.Equal(callsBefore, factory.CallCount);
        var entity = store.Latest("test-001");
        Assert.NotNull(entity);
        Assert.Equal("Completed", entity.Status);
        Assert.Contains("recovered", entity.Message);
    }

    [Fact]
    public async Task DeliverToErpAsync_NetworkFailure_ThrowsException()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory { ThrowOnCall = new HttpRequestException("Connection refused") };
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.DeliverToErpAsync(SampleOrder, "test-001", CancellationToken.None));

        Assert.Contains("Connection refused", ex.Message);
        var entity = store.Latest("test-001");
        Assert.NotNull(entity);
        Assert.Equal("Processing", entity.Status);
    }

    [Fact]
    public async Task DeliverToErpAsync_CancellationBeforeErpCall_ThrowsOperationCanceled()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            service.DeliverToErpAsync(SampleOrder, "test-001", cts.Token));
    }

    [Fact]
    public async Task DeliverToErpAsync_EmptyEventId_DoesNotThrow()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);
        var orderWithEmptyEventId = new EnrichedOrder
        {
            CorrelationId = SampleOrder.CorrelationId,
            EventId = string.Empty,
            SourceSystem = SampleOrder.SourceSystem,
            EventType = SampleOrder.EventType,
            ProductCode = SampleOrder.ProductCode,
            Quantity = SampleOrder.Quantity,
            TotalAmount = SampleOrder.TotalAmount,
            DeliveryPriority = SampleOrder.DeliveryPriority
        };

        var result = await service.DeliverToErpAsync(orderWithEmptyEventId, "test-empty", CancellationToken.None);

        Assert.Equal(DeliveryResult.Accepted, result);
    }

    [Fact]
    public async Task DeliverToErpAsync_NonJsonErrorBody_SetsFailedWithBody()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory(HttpStatusCode.InternalServerError, "<html>Server Error</html>");
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);

        var result = await service.DeliverToErpAsync(SampleOrder, "test-001", CancellationToken.None);

        Assert.Equal(DeliveryResult.Rejected, result);
        var entity = store.Latest("test-001");
        Assert.NotNull(entity);
        Assert.Equal("Failed", entity.Status);
        Assert.Contains("<html>", entity.Message);
    }

    [Fact]
    public async Task DeliverToErpAsync_StatusSequence_ProcessingThenCompleted()
    {
        var store = new InMemoryStatusStore();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ErpEndpointUrl"] = "http://localhost:5215/api/erp/deliveries"
            })
            .Build();
        var factory = new FakeHttpClientFactory(HttpStatusCode.OK);
        var idempotency = new FakeIdempotencyService();
        var service = CreateService(store, config, factory, idempotency);

        await service.DeliverToErpAsync(SampleOrder, "test-seq", CancellationToken.None);

        var statuses = store.AllWrites
            .Where(e => e.RowKey == "test-seq")
            .Select(e => e.Status)
            .ToList();

        Assert.Equal(["Processing", "Completed"], statuses);
    }

    private static OrderDeliveryService CreateService(
        InMemoryStatusStore store,
        IConfiguration config,
        FakeHttpClientFactory factory,
        FakeIdempotencyService idempotency)
    {
        return new OrderDeliveryService(
            store,
            idempotency,
            config,
            factory,
            NullLogger<OrderDeliveryService>.Instance);
    }
}
