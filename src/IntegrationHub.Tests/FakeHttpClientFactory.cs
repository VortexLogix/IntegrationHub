using System.Net;
using System.Text;
using System.Text.Json;

namespace IntegrationHub.Tests;

public sealed class FakeHttpClientFactory(HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "{}") : IHttpClientFactory
{
    public int CallCount { get; private set; }
    public Exception? ThrowOnCall { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public HttpClient CreateClient(string name)
    {
        var handler = new FakeHttpMessageHandler(statusCode, responseBody, this);
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody, FakeHttpClientFactory owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            owner.CallCount++;

            if (owner.ThrowOnCall is not null)
            {
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                throw owner.ThrowOnCall;
            }

            if (owner.Delay > TimeSpan.Zero)
            {
                await Task.Delay(owner.Delay, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }
}
