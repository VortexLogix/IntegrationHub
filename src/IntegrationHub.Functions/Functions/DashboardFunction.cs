using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace IntegrationHub.Functions.Functions;

[ExcludeFromCodeCoverage]
public sealed class DashboardFunction
{
    [Function("DashboardFunction")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        var content = await File.ReadAllTextAsync(scriptPath).ConfigureAwait(false);
        
        await response.WriteStringAsync(content).ConfigureAwait(false);
        return response;
    }
}
