namespace IntegrationHub.Functions.Interfaces;

public interface INotificationService
{
    Task SendDeadLetterAlertAsync(
        string messageId,
        string correlationId,
        string? deadLetterReason,
        string? deadLetterDescription,
        CancellationToken cancellationToken);

    Task SendFailureAlertAsync(
        string alertType,
        string correlationId,
        string message,
        CancellationToken cancellationToken);
}
