using Azure.Data.Tables;
using Azure.Identity;
using IntegrationHub.Functions.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton<TableServiceClient>(_ =>
{
    var accountName = builder.Configuration["AzureWebJobsStorage__accountName"];
    if (!string.IsNullOrWhiteSpace(accountName))
    {
        var tableUri = new Uri($"https://{accountName}.table.core.windows.net");
        return new TableServiceClient(tableUri, new DefaultAzureCredential());
    }

    var connectionString = builder.Configuration["AzureWebJobsStorage"]
        ?? throw new InvalidOperationException(
            "Neither AzureWebJobsStorage__accountName nor AzureWebJobsStorage is configured.");

    return new TableServiceClient(connectionString);
});

builder.Services.AddSingleton<IStatusStore, TableStatusStore>();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
