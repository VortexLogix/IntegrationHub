using Azure;
using Azure.Data.Tables;
using IntegrationHub.Api.Interfaces;

namespace IntegrationHub.Api.Services;

public sealed class TableStatusStore : IStatusStore
{
    private readonly TableClient _tableClient;

    public TableStatusStore(TableServiceClient tableServiceClient, IConfiguration configuration)
    {
        var tableName = configuration["StatusTableName"] ?? "OrderStatus";
        _tableClient = tableServiceClient.GetTableClient(tableName);
    }

    public async Task<OrderStatusEntity?> GetStatusAsync(string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var filter = TableClient.CreateQueryFilter($"RowKey eq {correlationId}");
            var results = _tableClient.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken);

            await foreach (var entity in results.ConfigureAwait(false))
            {
                return new OrderStatusEntity
                {
                    CorrelationId = entity.RowKey,
                    Status = entity.GetString("Status") ?? string.Empty,
                    Message = entity.GetString("Message") ?? string.Empty,
                    SourceSystem = entity.GetString("SourceSystem") ?? string.Empty,
                    EventType = entity.GetString("EventType") ?? string.Empty,
                    LastUpdatedUtc = entity.GetDateTimeOffset("LastUpdatedUtc") ?? DateTimeOffset.UtcNow
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
