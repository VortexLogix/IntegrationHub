using Azure.Identity;
using Azure.Messaging.ServiceBus;
using IntegrationHub.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IntegrationHub.Functions.Functions;

public sealed class DeadLetterHandlerFunction
{
    private readonly IConfiguration _configuration;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DeadLetterHandlerFunction> _logger;

    public DeadLetterHandlerFunction(
        IConfiguration configuration,
        INotificationService notificationService,
        ILogger<DeadLetterHandlerFunction> logger)
    {
        _configuration = configuration;
        _notificationService = notificationService;
        _logger = logger;
    }

    [Function("DeadLetterHandlerFunction")]
    public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        var fullyQualifiedNamespace = _configuration["ServiceBusConnection__fullyQualifiedNamespace"];
        var queueName = _configuration["ServiceBusOrdersQueueName"] ?? "orders";

        if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        {
            _logger.LogWarning("ServiceBusConnection__fullyQualifiedNamespace is not configured; dead-letter handler skipped.");
            return;
        }

        await using var client = new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());
        await using var receiver = client.CreateReceiver(queueName, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter
        });

        var processedCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                break;
            }

            var correlationId = message.CorrelationId ?? message.MessageId;

            _logger.LogError(
                "Dead-letter message detected. MessageId: {MessageId}, CorrelationId: {CorrelationId}, Reason: {Reason}, Description: {Description}",
                message.MessageId,
                correlationId,
                message.DeadLetterReason,
                message.DeadLetterErrorDescription);

            // Send webhook alert so the operations team is notified immediately.
            await _notificationService.SendDeadLetterAlertAsync(
                message.MessageId,
                correlationId,
                message.DeadLetterReason,
                message.DeadLetterErrorDescription,
                cancellationToken).ConfigureAwait(false);

            await receiver.CompleteMessageAsync(message, cancellationToken).ConfigureAwait(false);
            processedCount++;
        }

        _logger.LogInformation(
            "Dead-letter queue scan completed at {UtcNow}. Messages processed: {Count}.",
            DateTimeOffset.UtcNow,
            processedCount);
    }
}
