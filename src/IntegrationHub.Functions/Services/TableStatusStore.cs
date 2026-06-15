using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace IntegrationHub.Functions.Services;

[ExcludeFromCodeCoverage]
public sealed class TableStatusStore : IStatusStore
{
    private readonly TableClient _tableClient;

    public TableStatusStore(TableServiceClient tableServiceClient, IConfiguration configuration)
    {
        _tableClient = tableServiceClient.GetTableClient(configuration["StatusTableName"] ?? "OrderStatus");
        _tableClient.CreateIfNotExistsAsync().GetAwaiter().GetResult();
    }

    public async Task SetStatusAsync(
        string correlationId,
        string sourceSystem,
        string status,
        string message,
        string eventType,
        CancellationToken cancellationToken)
    {
        var entity = new TableEntity(sourceSystem, correlationId)
        {
            ["Status"] = status,
            ["Message"] = message,
            ["SourceSystem"] = sourceSystem,
            ["EventType"] = eventType,
            ["LastUpdatedUtc"] = DateTimeOffset.UtcNow
        };

        await _tableClient.UpsertEntityAsync(entity, mode: TableUpdateMode.Replace, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrderStatusEntity?> GetStatusAsync(string correlationId, string? sourceSystem = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = sourceSystem is not null
                ? TableClient.CreateQueryFilter($"PartitionKey eq {sourceSystem} and RowKey eq {correlationId}")
                : TableClient.CreateQueryFilter($"RowKey eq {correlationId}");
            var results = _tableClient.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken);

            await foreach (var entity in results.ConfigureAwait(false))
            {
                return new OrderStatusEntity
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey,
                    Status = entity.GetString("Status") ?? string.Empty,
                    Message = entity.GetString("Message") ?? string.Empty,
                    SourceSystem = entity.GetString("SourceSystem") ?? string.Empty,
                    EventType = entity.GetString("EventType") ?? string.Empty,
                    LastUpdatedUtc = entity.GetDateTimeOffset("LastUpdatedUtc") ?? DateTimeOffset.UtcNow,
                    Timestamp = entity.Timestamp
                };
            }
        }
        catch (RequestFailedException)
        {
            return null;
        }

        return null;
    }
}
