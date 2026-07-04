using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace IntegrationHub.Functions.Functions;

public sealed class DashboardFunction
{
    [Function("DashboardFunction")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard")] HttpRequest req)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        
        if (!File.Exists(scriptPath))
        {
            return new NotFoundObjectResult("Dashboard index.html not found.");
        }

        var content = await File.ReadAllTextAsync(scriptPath).ConfigureAwait(false);
        
        return new ContentResult
        {
            Content = content,
            ContentType = "text/html; charset=utf-8",
            StatusCode = StatusCodes.Status200OK
        };
    }
}
