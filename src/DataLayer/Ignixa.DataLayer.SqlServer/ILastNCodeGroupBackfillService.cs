namespace Ignixa.DataLayer.SqlServer;

public interface ILastNCodeGroupBackfillService
{
    Task EnableScopeAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        CancellationToken cancellationToken);

    Task BuildAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        int batchSize,
        CancellationToken cancellationToken);
}
