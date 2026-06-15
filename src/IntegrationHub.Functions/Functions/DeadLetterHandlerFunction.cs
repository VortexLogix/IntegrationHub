using System.Diagnostics.CodeAnalysis;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using IntegrationHub.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IntegrationHub.Functions.Functions;

[ExcludeFromCodeCoverage]
public sealed class DeadLetterHandlerFunction(
    IConfiguration configuration,
    INotificationService notificationService,
    ILogger<DeadLetterHandlerFunction> logger)
{
    [Function("DeadLetterHandlerFunction")]
    public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        var fullyQualifiedNamespace = configuration["ServiceBusConnection__fullyQualifiedNamespace"];
        var queueName = configuration["ServiceBusOrdersQueueName"] ?? "orders";

        if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        {
            logger.LogWarning("ServiceBusConnection__fullyQualifiedNamespace is not configured; dead-letter handler skipped.");
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

            logger.LogError(
                "Dead-letter message detected. MessageId: {MessageId}, CorrelationId: {CorrelationId}, Reason: {Reason}, Description: {Description}",
                message.MessageId,
                correlationId,
                message.DeadLetterReason,
                message.DeadLetterErrorDescription);

            await notificationService.SendDeadLetterAlertAsync(
                message.MessageId,
                correlationId,
                message.DeadLetterReason,
                message.DeadLetterErrorDescription,
                cancellationToken).ConfigureAwait(false);

            await receiver.CompleteMessageAsync(message, cancellationToken).ConfigureAwait(false);
            processedCount++;
        }

        logger.LogInformation(
            "Dead-letter queue scan completed at {UtcNow}. Messages processed: {Count}.",
            DateTimeOffset.UtcNow,
            processedCount);
    }
}
