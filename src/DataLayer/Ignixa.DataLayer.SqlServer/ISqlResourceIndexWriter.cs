namespace Ignixa.DataLayer.SqlServer;

public interface ISqlResourceIndexWriter
{
    Task<int> MergeAsync(int tenantId, SqlResourceMergeRequest request, CancellationToken cancellationToken);

    Task<int> ReindexAsync(int tenantId, SqlResourceReindexRequest request, CancellationToken cancellationToken);

    Task HardDeleteAsync(
        int tenantId,
        short resourceTypeId,
        string resourceId,
        bool keepCurrentVersion,
        bool isResourceChangeCaptureEnabled,
        CancellationToken cancellationToken);
}
