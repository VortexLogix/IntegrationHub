using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddHttpClient<IntegrationHub.Functions.Services.INotificationService,
                               IntegrationHub.Functions.Services.HttpNotificationService>();

// BlobServiceClient: use Managed Identity in Azure (accountName setting),
// fall back to connection string for local Azurite dev.
builder.Services.AddSingleton<BlobServiceClient>(_ =>
{
    var accountName = builder.Configuration["AzureWebJobsStorage__accountName"];
    if (!string.IsNullOrWhiteSpace(accountName))
    {
        // Azure: Managed Identity — no stored credentials.
        var blobUri = new Uri($"https://{accountName}.blob.core.windows.net");
        return new BlobServiceClient(blobUri, new DefaultAzureCredential());
    }

    // Local dev: Azurite connection string (UseDevelopmentStorage=true).
    var connectionString = builder.Configuration["AzureWebJobsStorage"]
        ?? throw new InvalidOperationException(
            "Neither AzureWebJobsStorage__accountName nor AzureWebJobsStorage is configured.");

    return new BlobServiceClient(connectionString);
});

builder.Services.AddSingleton<IntegrationHub.Functions.Services.IIdempotencyService,
                               IntegrationHub.Functions.Services.BlobIdempotencyService>();
builder.Services.AddSingleton<IntegrationHub.Functions.Services.IClaimCheckStore,
                               IntegrationHub.Functions.Services.BlobClaimCheckStore>();
builder.Services.AddSingleton<IntegrationHub.Functions.Services.IEnrichmentService,
                               IntegrationHub.Functions.Services.EnrichmentService>();

builder.Build().Run();

