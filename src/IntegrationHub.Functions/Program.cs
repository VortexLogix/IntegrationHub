using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddHttpClient<IntegrationHub.Functions.Services.INotificationService,
                               IntegrationHub.Functions.Services.HttpNotificationService>();

builder.Services.AddSingleton<BlobServiceClient>(_ =>
{
    var accountName = Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName");
    if (!string.IsNullOrWhiteSpace(accountName))
    {
        var blobUri = new Uri($"https://{accountName}.blob.core.windows.net");
        return new BlobServiceClient(blobUri, new DefaultAzureCredential());
    }

    var connectionString = builder.Configuration["AzureWebJobsStorage"]
        ?? throw new InvalidOperationException(
            "Neither AzureWebJobsStorage__accountName nor AzureWebJobsStorage is configured.");

    return new BlobServiceClient(connectionString);
});

builder.Services.AddSingleton<TableServiceClient>(_ =>
{
    var connectionString = builder.Configuration["AzureWebJobsStorage"];
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        return new TableServiceClient(connectionString);
    }

    var accountName = Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName")
        ?? throw new InvalidOperationException(
            "Neither AzureWebJobsStorage nor AzureWebJobsStorage__accountName is configured.");

    var tableUri = new Uri($"https://{accountName}.table.core.windows.net");
    return new TableServiceClient(tableUri, new DefaultAzureCredential());
});

builder.Services.AddSingleton<IntegrationHub.Functions.Services.IIdempotencyService,
                               IntegrationHub.Functions.Services.BlobIdempotencyService>();
builder.Services.AddSingleton<IntegrationHub.Functions.Services.IClaimCheckStore,
                               IntegrationHub.Functions.Services.BlobClaimCheckStore>();
builder.Services.AddSingleton<IntegrationHub.Functions.Services.IEnrichmentService,
                               IntegrationHub.Functions.Services.EnrichmentService>();
builder.Services.AddSingleton<IntegrationHub.Functions.Services.IStatusStore,
                               IntegrationHub.Functions.Services.TableStatusStore>();
builder.Services.AddSingleton<IntegrationHub.Functions.Services.IOrderDeliveryService,
                               IntegrationHub.Functions.Services.OrderDeliveryService>();

builder.Build().Run();

