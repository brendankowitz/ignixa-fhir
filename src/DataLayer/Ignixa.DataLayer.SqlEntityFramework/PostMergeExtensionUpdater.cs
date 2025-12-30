// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Text;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Updates extension columns on search parameter tables after MergeResources completes.
/// The TVPs used by MergeResources only include core columns to maintain compatibility
/// with the original stored procedure. Extension columns (IdentifierType*, Version, Fragment)
/// are updated separately via this service using batched parameterized SQL.
/// </summary>
public class PostMergeExtensionUpdater
{
    private const int BatchSize = 100;
    private readonly FhirDbContext _context;
    private readonly ILogger<PostMergeExtensionUpdater> _logger;

    public PostMergeExtensionUpdater(FhirDbContext context, ILogger<PostMergeExtensionUpdater> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        _logger.LogDebug("Updating {Count} TokenSearchParam extension records in batches", extensionList.Count);

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

            await _context.Database.ExecuteSqlRawAsync(
                sqlBuilder.ToString(),
                parameters,
                cancellationToken);
        }

        _logger.LogInformation("Updated {Count} TokenSearchParam extension records", extensionList.Count);
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

        _logger.LogDebug("Updating {Count} UriSearchParam extension records in batches", extensionList.Count);

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

            await _context.Database.ExecuteSqlRawAsync(
                sqlBuilder.ToString(),
                parameters,
                cancellationToken);
        }

        _logger.LogInformation("Updated {Count} UriSearchParam extension records", extensionList.Count);
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
