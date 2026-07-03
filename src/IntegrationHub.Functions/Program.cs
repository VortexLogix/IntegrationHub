using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using IntegrationHub.Functions.Interfaces;
using IntegrationHub.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddHttpClient<INotificationService, HttpNotificationService>();

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

builder.Services.AddSingleton<IIdempotencyService, BlobIdempotencyService>();
builder.Services.AddSingleton<IClaimCheckStore, BlobClaimCheckStore>();
builder.Services.AddSingleton<IEnrichmentService, EnrichmentService>();
builder.Services.AddSingleton<IStatusStore, TableStatusStore>();
builder.Services.AddSingleton<IOrderDeliveryService, OrderDeliveryService>();

builder.Build().Run();
