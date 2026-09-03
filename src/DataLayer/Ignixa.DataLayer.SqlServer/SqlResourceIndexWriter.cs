using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SqlResourceIndexWriter : ISqlResourceIndexWriter
{
    private readonly ISqlExecutionService _executionService;

    public SqlResourceIndexWriter(ISqlExecutionService executionService)
    {
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
    }

    public async Task<int> MergeAsync(
        int tenantId,
        SqlResourceMergeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var command = new SqlCommand("dbo.MergeResourcesAndMaintainLastNGroups")
        {
            CommandType = CommandType.StoredProcedure,
        };

        var affectedRows = new SqlParameter("@AffectedRows", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };

        command.Parameters.Add(affectedRows);
        command.Parameters.Add(new SqlParameter("@RaiseExceptionOnConflict", SqlDbType.Bit) { Value = request.RaiseExceptionOnConflict });
        command.Parameters.Add(new SqlParameter("@IsResourceChangeCaptureEnabled", SqlDbType.Bit) { Value = request.IsResourceChangeCaptureEnabled });
        command.Parameters.Add(new SqlParameter("@TransactionId", SqlDbType.BigInt) { Value = request.TransactionId });
        command.Parameters.Add(new SqlParameter("@SingleTransaction", SqlDbType.Bit) { Value = request.SingleTransaction });
        AddStructuredParameters(command, request.Batch, isMerge: true);

        await _executionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken, disableRetries: true);
        return Convert.ToInt32(affectedRows.Value);
    }

    public async Task<int> ReindexAsync(
        int tenantId,
        SqlResourceReindexRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var command = new SqlCommand("dbo.UpdateResourceSearchParamsAndMaintainLastNGroups")
        {
            CommandType = CommandType.StoredProcedure,
        };

        var failedResources = new SqlParameter("@FailedResources", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };

        command.Parameters.Add(failedResources);
        AddStructuredParameters(command, request.Batch, isMerge: false);

        await _executionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken, disableRetries: true);
        return Convert.ToInt32(failedResources.Value);
    }

    public async Task HardDeleteAsync(
        int tenantId,
        short resourceTypeId,
        string resourceId,
        bool keepCurrentVersion,
        bool isResourceChangeCaptureEnabled,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceId);

        using var command = new SqlCommand("dbo.HardDeleteResourceAndMaintainLastNGroups")
        {
            CommandType = CommandType.StoredProcedure,
        };

        command.Parameters.Add(new SqlParameter("@ResourceTypeId", SqlDbType.SmallInt) { Value = resourceTypeId });
        command.Parameters.Add(new SqlParameter("@ResourceId", SqlDbType.VarChar) { Value = resourceId });
        command.Parameters.Add(new SqlParameter("@KeepCurrentVersion", SqlDbType.Bit) { Value = keepCurrentVersion });
        command.Parameters.Add(new SqlParameter("@IsResourceChangeCaptureEnabled", SqlDbType.Bit) { Value = isResourceChangeCaptureEnabled });

        await _executionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken, disableRetries: true);
    }

    private static void AddStructuredParameters(SqlCommand command, SqlResourceIndexBatch batch, bool isMerge)
    {
        ArgumentNullException.ThrowIfNull(batch);

        AddStructuredParameter(command, "@Resources", "dbo.ResourceList", batch.Resources);
        AddStructuredParameter(command, "@ResourceWriteClaims", "dbo.ResourceWriteClaimList", batch.ResourceWriteClaims);
        AddStructuredParameter(command, "@ReferenceSearchParams", "dbo.ReferenceSearchParamList", batch.ReferenceSearchParams);
        AddStructuredParameter(command, "@TokenSearchParams", "dbo.TokenSearchParamList", batch.TokenSearchParams);
        AddStructuredParameter(command, "@TokenTexts", "dbo.TokenTextList", batch.TokenTexts);
        AddStructuredParameter(command, "@StringSearchParams", "dbo.StringSearchParamList", batch.StringSearchParams);
        AddStructuredParameter(command, "@UriSearchParams", "dbo.UriSearchParamList", batch.UriSearchParams);
        AddStructuredParameter(command, "@NumberSearchParams", "dbo.NumberSearchParamList", batch.NumberSearchParams);
        AddStructuredParameter(command, "@QuantitySearchParams", "dbo.QuantitySearchParamList", batch.QuantitySearchParams);
        AddStructuredParameter(
            command,
            isMerge ? "@DateTimeSearchParms" : "@DateTimeSearchParams",
            "dbo.DateTimeSearchParamList",
            batch.DateTimeSearchParams);
        AddStructuredParameter(command, "@ReferenceTokenCompositeSearchParams", "dbo.ReferenceTokenCompositeSearchParamList", batch.ReferenceTokenCompositeSearchParams);
        AddStructuredParameter(command, "@TokenTokenCompositeSearchParams", "dbo.TokenTokenCompositeSearchParamList", batch.TokenTokenCompositeSearchParams);
        AddStructuredParameter(command, "@TokenDateTimeCompositeSearchParams", "dbo.TokenDateTimeCompositeSearchParamList", batch.TokenDateTimeCompositeSearchParams);
        AddStructuredParameter(command, "@TokenQuantityCompositeSearchParams", "dbo.TokenQuantityCompositeSearchParamList", batch.TokenQuantityCompositeSearchParams);
        AddStructuredParameter(command, "@TokenStringCompositeSearchParams", "dbo.TokenStringCompositeSearchParamList", batch.TokenStringCompositeSearchParams);
        AddStructuredParameter(command, "@TokenNumberNumberCompositeSearchParams", "dbo.TokenNumberNumberCompositeSearchParamList", batch.TokenNumberNumberCompositeSearchParams);
    }

    private static void AddStructuredParameter(
        SqlCommand command,
        string name,
        string typeName,
        IReadOnlyList<SqlDataRecord>? rows)
    {
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Structured)
        {
            TypeName = typeName,
            Value = rows,
        });
    }
}
