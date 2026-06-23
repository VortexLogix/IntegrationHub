using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;

namespace IntegrationHub.Functions.Services;

[ExcludeFromCodeCoverage]
public sealed class HttpNotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly string? _webhookUrl;
    private readonly ILogger<HttpNotificationService> _logger;

    public HttpNotificationService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<HttpNotificationService> logger)
    {
        _httpClient = httpClient;
        _webhookUrl = configuration["NotificationWebhookUrl"];
        _logger = logger;
    }

    public async Task SendDeadLetterAlertAsync(
        string messageId,
        string correlationId,
        string? deadLetterReason,
        string? deadLetterDescription,
        CancellationToken cancellationToken)
    {
        await PostAlertAsync(new
        {
            alert = "DeadLetterDetected",
            messageId,
            correlationId,
            reason = deadLetterReason ?? "Unknown",
            description = deadLetterDescription ?? string.Empty,
            timestampUtc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendFailureAlertAsync(
        string alertType,
        string correlationId,
        string message,
        CancellationToken cancellationToken)
    {
        await PostAlertAsync(new
        {
            alert = alertType,
            correlationId,
            message,
            timestampUtc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task PostAlertAsync(object payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl) || _webhookUrl.StartsWith("@Microsoft.KeyVault"))
        {
            _logger.LogWarning("NotificationWebhookUrl is not configured — alert skipped.");
            return;
        }

        try
        {
            using var response = await _httpClient
                .PostAsJsonAsync(_webhookUrl, payload, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Notification webhook returned {StatusCode}. Alert may not have been delivered.",
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Never let a notification failure break the primary processing flow.
            _logger.LogWarning(ex, "Failed to deliver notification webhook alert.");
        }
    }
}
