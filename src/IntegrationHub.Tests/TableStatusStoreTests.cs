using IntegrationHub.Functions.Services;
using Xunit;

namespace IntegrationHub.Tests;

public sealed class TableStatusStoreTests
{
    [Fact]
    public void InterfaceContract_CanBeReplacedWithInMemoryImplementation()
    {
        IStatusStore store = new InMemoryStatusStore();
        Assert.NotNull(store);
    }

    [Fact]
    public async Task InMemoryStatusStore_RoundTrip_PreservesAllFields()
    {
        var store = new InMemoryStatusStore();
        var ct = CancellationToken.None;

        await store.SetStatusAsync("corr-001", "CRM", "Processing", "Working on it", "OrderCreated", ct);
        var result = await store.GetStatusAsync("corr-001", cancellationToken: ct);

        Assert.NotNull(result);
        Assert.Equal("corr-001", result.RowKey);
        Assert.Equal("CRM", result.PartitionKey);
        Assert.Equal("CRM", result.SourceSystem);
        Assert.Equal("Processing", result.Status);
        Assert.Equal("Working on it", result.Message);
        Assert.Equal("OrderCreated", result.EventType);
    }

    [Fact]
    public async Task InMemoryStatusStore_GetStatus_NotFound_ReturnsNull()
    {
        var store = new InMemoryStatusStore();
        var result = await store.GetStatusAsync("non-existent", cancellationToken: CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStatusStore_SetStatus_OverwritesExisting()
    {
        var store = new InMemoryStatusStore();
        var ct = CancellationToken.None;

        await store.SetStatusAsync("corr-001", "CRM", "Processing", "Step 1", "OrderCreated", ct);
        await store.SetStatusAsync("corr-001", "CRM", "Completed", "Step 2 done", "OrderCreated", ct);

        var result = await store.GetStatusAsync("corr-001", cancellationToken: ct);
        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);
        Assert.Equal("Step 2 done", result.Message);
    }

    [Fact]
    public async Task InMemoryStatusStore_GetStatus_WithSourceSystem_FiltersByPartition()
    {
        var store = new InMemoryStatusStore();
        var ct = CancellationToken.None;

        await store.SetStatusAsync("corr-001", "CRM", "Processing", "CRM step", "OrderCreated", ct);
        await store.SetStatusAsync("corr-001", "ERP", "Completed", "ERP step", "OrderCreated", ct);

        var crmResult = await store.GetStatusAsync("corr-001", "CRM", ct);
        Assert.NotNull(crmResult);
        Assert.Equal("CRM", crmResult.PartitionKey);
        Assert.Equal("Processing", crmResult.Status);

        var erpResult = await store.GetStatusAsync("corr-001", "ERP", ct);
        Assert.NotNull(erpResult);
        Assert.Equal("ERP", erpResult.PartitionKey);
        Assert.Equal("Completed", erpResult.Status);
    }

    [Fact]
    public async Task InMemoryStatusStore_GetStatus_WithSourceSystemNotFound_ReturnsNull()
    {
        var store = new InMemoryStatusStore();
        var ct = CancellationToken.None;

        await store.SetStatusAsync("corr-001", "CRM", "Processing", "CRM step", "OrderCreated", ct);

        var result = await store.GetStatusAsync("corr-001", "ERP", ct);
        Assert.Null(result);
    }
}
