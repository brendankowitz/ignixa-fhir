using System.Data;
using System.Text;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Updates extension columns on search parameter tables after MergeResources completes.
/// The TVPs used by MergeResources only include core columns to maintain compatibility
/// with the original stored procedure. Extension columns (IdentifierType*, Version, Fragment)
/// are updated separately via this service using batched parameterized SQL.
/// </summary>
public class SqlServerPostMergeExtensionUpdater(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    ILogger<SqlServerPostMergeExtensionUpdater> logger)
{
    private const int BatchSize = 100;

    /// <summary>
    /// Updates TokenSearchParam extension columns (IdentifierTypeSystemId, IdentifierTypeCode)
    /// for rows that were just inserted by MergeResources.
    /// Uses batched updates to minimize database roundtrips.
    /// </summary>
    public async Task UpdateTokenSearchParamExtensionsAsync(
        IEnumerable<TokenSearchParamExtensionData> extensions,
        CancellationToken cancellationToken = default)
    {
        var extensionList = extensions.ToList();
        if (extensionList.Count == 0)
        {
            return;
        }

        logger.LogDebug("Updating {Count} TokenSearchParam extension records in batches", extensionList.Count);

        foreach (var batch in extensionList.Chunk(BatchSize))
        {
            var sqlBuilder = new StringBuilder();
            var parameters = new List<SqlParameter>();

            for (var i = 0; i < batch.Length; i++)
            {
                var ext = batch[i];

                parameters.Add(new SqlParameter($"@ResourceTypeId{i}", SqlDbType.SmallInt) { Value = ext.ResourceTypeId });
                parameters.Add(new SqlParameter($"@ResourceSurrogateId{i}", SqlDbType.BigInt) { Value = ext.ResourceSurrogateId });
                parameters.Add(new SqlParameter($"@SearchParamId{i}", SqlDbType.SmallInt) { Value = ext.SearchParamId });
                parameters.Add(new SqlParameter($"@SystemId{i}", SqlDbType.Int) { Value = ext.SystemId.HasValue ? ext.SystemId.Value : DBNull.Value });
                parameters.Add(new SqlParameter($"@Code{i}", SqlDbType.VarChar, 256) { Value = ext.Code });
                parameters.Add(new SqlParameter($"@IdentifierTypeSystemId{i}", SqlDbType.Int) { Value = ext.IdentifierTypeSystemId.HasValue ? ext.IdentifierTypeSystemId.Value : DBNull.Value });
                parameters.Add(new SqlParameter($"@IdentifierTypeCode{i}", SqlDbType.VarChar, 256) { Value = ext.IdentifierTypeCode ?? (object)DBNull.Value });

                sqlBuilder.AppendLine($@"
UPDATE dbo.TokenSearchParam
SET IdentifierTypeSystemId = @IdentifierTypeSystemId{i},
    IdentifierTypeCode = @IdentifierTypeCode{i}
WHERE ResourceTypeId = @ResourceTypeId{i}
  AND ResourceSurrogateId = @ResourceSurrogateId{i}
  AND SearchParamId = @SearchParamId{i}
  AND ((@SystemId{i} IS NULL AND SystemId IS NULL) OR SystemId = @SystemId{i})
  AND Code = @Code{i};");
            }

            // CA2100 suppressed: SQL is constructed from safe loop-variable suffixes (0, 1, 2, ...), not user input
#pragma warning disable CA2100
            using var command = new SqlCommand(sqlBuilder.ToString());
#pragma warning restore CA2100
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            await sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);
        }

        logger.LogInformation("Updated {Count} TokenSearchParam extension records", extensionList.Count);
    }

    /// <summary>
    /// Updates UriSearchParam extension columns (Version, Fragment)
    /// for rows that were just inserted by MergeResources.
    /// Uses batched updates to minimize database roundtrips.
    /// </summary>
    public async Task UpdateUriSearchParamExtensionsAsync(
        IEnumerable<UriSearchParamExtensionData> extensions,
        CancellationToken cancellationToken = default)
    {
        var extensionList = extensions.ToList();
        if (extensionList.Count == 0)
        {
            return;
        }

        logger.LogDebug("Updating {Count} UriSearchParam extension records in batches", extensionList.Count);

        foreach (var batch in extensionList.Chunk(BatchSize))
        {
            var sqlBuilder = new StringBuilder();
            var parameters = new List<SqlParameter>();

            for (var i = 0; i < batch.Length; i++)
            {
                var ext = batch[i];

                parameters.Add(new SqlParameter($"@ResourceTypeId{i}", SqlDbType.SmallInt) { Value = ext.ResourceTypeId });
                parameters.Add(new SqlParameter($"@ResourceSurrogateId{i}", SqlDbType.BigInt) { Value = ext.ResourceSurrogateId });
                parameters.Add(new SqlParameter($"@SearchParamId{i}", SqlDbType.SmallInt) { Value = ext.SearchParamId });
                parameters.Add(new SqlParameter($"@Uri{i}", SqlDbType.VarChar, 256) { Value = ext.Uri });
                parameters.Add(new SqlParameter($"@Version{i}", SqlDbType.NVarChar, 64) { Value = ext.Version ?? (object)DBNull.Value });
                parameters.Add(new SqlParameter($"@Fragment{i}", SqlDbType.NVarChar, 128) { Value = ext.Fragment ?? (object)DBNull.Value });

                sqlBuilder.AppendLine($@"
UPDATE dbo.UriSearchParam
SET Version = @Version{i},
    Fragment = @Fragment{i}
WHERE ResourceTypeId = @ResourceTypeId{i}
  AND ResourceSurrogateId = @ResourceSurrogateId{i}
  AND SearchParamId = @SearchParamId{i}
  AND Uri = @Uri{i};");
            }

            // CA2100 suppressed: SQL is constructed from safe loop-variable suffixes (0, 1, 2, ...), not user input
#pragma warning disable CA2100
            using var command = new SqlCommand(sqlBuilder.ToString());
#pragma warning restore CA2100
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            await sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);
        }

        logger.LogInformation("Updated {Count} UriSearchParam extension records", extensionList.Count);
    }

    /// <summary>
    /// Updates all extension columns in a single call after MergeResources completes.
    /// </summary>
    public async Task UpdateAllExtensionsAsync(
        IEnumerable<TokenSearchParamExtensionData> tokenExtensions,
        IEnumerable<UriSearchParamExtensionData> uriExtensions,
        CancellationToken cancellationToken = default)
    {
        await UpdateTokenSearchParamExtensionsAsync(tokenExtensions, cancellationToken);
        await UpdateUriSearchParamExtensionsAsync(uriExtensions, cancellationToken);
    }
}
