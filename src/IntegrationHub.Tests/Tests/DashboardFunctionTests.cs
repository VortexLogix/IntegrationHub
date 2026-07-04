using System.IO;
using System.Threading.Tasks;
using IntegrationHub.Functions.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace IntegrationHub.Tests.Tests;

public class DashboardFunctionTests
{
    [Fact]
    public async Task Run_ReturnsHtmlContent_WhenFileExists()
    {
        // Arrange
        var function = new DashboardFunction();
        var context = new DefaultHttpContext();
        var request = context.Request;

        // Ensure wwwroot exists for the test context
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(wwwroot))
        {
            Directory.CreateDirectory(wwwroot);
        }
        var indexHtml = Path.Combine(wwwroot, "index.html");
        await File.WriteAllTextAsync(indexHtml, "<html>Test Dashboard</html>");

        // Act
        var result = await function.Run(request);

        // Assert
        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal(200, contentResult.StatusCode);
        Assert.Equal("text/html; charset=utf-8", contentResult.ContentType);
        Assert.Equal("<html>Test Dashboard</html>", contentResult.Content);
        
        // Cleanup
        File.Delete(indexHtml);
    }
}
