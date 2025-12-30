// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Updates extension columns on search parameter tables after MergeResources completes.
/// The TVPs used by MergeResources only include core columns to maintain compatibility
/// with the original stored procedure. Extension columns (IdentifierType*, Version, Fragment)
/// are updated separately via this service using parameterized SQL.
/// </summary>
public class PostMergeExtensionUpdater
{
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

        _logger.LogDebug("Updating {Count} TokenSearchParam extension records", extensionList.Count);

        foreach (var ext in extensionList)
        {
            var parameters = new[]
            {
                new SqlParameter("@ResourceTypeId", SqlDbType.SmallInt) { Value = ext.ResourceTypeId },
                new SqlParameter("@ResourceSurrogateId", SqlDbType.BigInt) { Value = ext.ResourceSurrogateId },
                new SqlParameter("@SearchParamId", SqlDbType.SmallInt) { Value = ext.SearchParamId },
                new SqlParameter("@SystemId", SqlDbType.Int) { Value = ext.SystemId.HasValue ? ext.SystemId.Value : DBNull.Value },
                new SqlParameter("@Code", SqlDbType.VarChar, 256) { Value = ext.Code },
                new SqlParameter("@IdentifierTypeSystemId", SqlDbType.Int) { Value = ext.IdentifierTypeSystemId.HasValue ? ext.IdentifierTypeSystemId.Value : DBNull.Value },
                new SqlParameter("@IdentifierTypeCode", SqlDbType.VarChar, 256) { Value = ext.IdentifierTypeCode ?? (object)DBNull.Value },
            };

            // Match on the composite key (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code)
            // Use ISNULL comparison pattern for nullable SystemId column
            await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE dbo.TokenSearchParam
                  SET IdentifierTypeSystemId = @IdentifierTypeSystemId,
                      IdentifierTypeCode = @IdentifierTypeCode
                  WHERE ResourceTypeId = @ResourceTypeId
                    AND ResourceSurrogateId = @ResourceSurrogateId
                    AND SearchParamId = @SearchParamId
                    AND ((@SystemId IS NULL AND SystemId IS NULL) OR SystemId = @SystemId)
                    AND Code = @Code",
                parameters,
                cancellationToken);
        }

        _logger.LogInformation("Updated {Count} TokenSearchParam extension records", extensionList.Count);
    }

    /// <summary>
    /// Updates UriSearchParam extension columns (Version, Fragment)
    /// for rows that were just inserted by MergeResources.
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

        _logger.LogDebug("Updating {Count} UriSearchParam extension records", extensionList.Count);

        foreach (var ext in extensionList)
        {
            var parameters = new[]
            {
                new SqlParameter("@ResourceTypeId", SqlDbType.SmallInt) { Value = ext.ResourceTypeId },
                new SqlParameter("@ResourceSurrogateId", SqlDbType.BigInt) { Value = ext.ResourceSurrogateId },
                new SqlParameter("@SearchParamId", SqlDbType.SmallInt) { Value = ext.SearchParamId },
                new SqlParameter("@Uri", SqlDbType.VarChar, 256) { Value = ext.Uri },
                new SqlParameter("@Version", SqlDbType.NVarChar, 64) { Value = ext.Version ?? (object)DBNull.Value },
                new SqlParameter("@Fragment", SqlDbType.NVarChar, 128) { Value = ext.Fragment ?? (object)DBNull.Value },
            };

            // Match on the composite PK (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
            await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE dbo.UriSearchParam
                  SET Version = @Version,
                      Fragment = @Fragment
                  WHERE ResourceTypeId = @ResourceTypeId
                    AND ResourceSurrogateId = @ResourceSurrogateId
                    AND SearchParamId = @SearchParamId
                    AND Uri = @Uri",
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
