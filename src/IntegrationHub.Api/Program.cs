using Azure.Data.Tables;
using Azure.Identity;
using IntegrationHub.Functions.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton<TableServiceClient>(_ =>
{
    var connectionString = builder.Configuration["AzureWebJobsStorage"];
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        return new TableServiceClient(connectionString);
    }

    var accountName = builder.Configuration["AzureWebJobsStorage__accountName"]
        ?? throw new InvalidOperationException(
            "Neither AzureWebJobsStorage nor AzureWebJobsStorage__accountName is configured.");

    var tableUri = new Uri($"https://{accountName}.table.core.windows.net");
    return new TableServiceClient(tableUri, new DefaultAzureCredential());
});

builder.Services.AddSingleton<IStatusStore, TableStatusStore>();

var app = builder.Build();

app.UseHttpsRedirection();

var options = new DefaultFilesOptions();
options.DefaultFileNames.Clear();
options.DefaultFileNames.Add("index.html");
app.UseDefaultFiles(options);
app.UseStaticFiles();

app.MapControllers();

app.Run();
