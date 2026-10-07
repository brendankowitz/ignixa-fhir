// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Ignixa.DataLayer.SqlServer.Indexing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.SemanticSearch;

/// <summary>
/// Resolves a semantic embedding model's <c>EmbeddingModelId</c> via <c>dbo.GetOrCreateEmbeddingModel</c>,
/// caching the result on the tenant-scoped <paramref name="referenceDataCache"/> this type is constructed
/// with (<see cref="SqlServerSearchIndexReferenceDataCache.TryGetEmbeddingModelIdFromCache"/> /
/// <see cref="SqlServerSearchIndexReferenceDataCache.CacheEmbeddingModelId"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the cache lives on that instance and not here.</b> This type is constructed per request, like
/// <c>SqlServerPostMergeExtensionUpdater</c> (see <c>SqlServerRepositoryFactory.CreateRepository</c>) --
/// not once per tenant for the process lifetime. An instance field on this type would therefore be rebuilt
/// and discarded on every request, caching nothing across requests. <paramref name="referenceDataCache"/>,
/// by contrast, is the one object <c>SqlServerSearchIndexCacheRegistry</c> owns per tenant for the process
/// lifetime (<c>GetOrCreateAsync</c>) and hands to every per-request caller -- the same object every other
/// tenant-scoped reference cache in this codebase is built on (docs/adr/adr-2510-caching-architecture.md's
/// Tenant scope). Caching there, rather than in a static field here, means
/// <c>SqlServerSearchIndexCacheRegistry.Invalidate(tenantId)</c> clears this mapping along with everything
/// else that cache holds -- which matters because, unlike system/quantity-code ids, a stale EmbeddingModelId
/// is not merely a missed optimization: <c>dbo.VectorSearchParam.EmbeddingModelId</c> is foreign-keyed to
/// <c>dbo.EmbeddingModel.EmbeddingModelId</c> (schema v5), so a cached id surviving a tenant database that
/// was dropped and re-provisioned under the same TenantId would fail every subsequent vector write with a
/// foreign-key violation instead of transparently recreating the row.
/// </para>
/// <para>
/// Concurrent misses for the same key are not de-duplicated: two requests racing on a brand-new ModelKey
/// may both call the stored procedure. That is safe and intentional -- see
/// <c>GetOrCreateEmbeddingModel.sql</c>'s own header for why it tolerates and resolves exactly that race,
/// returning the same id to every caller.
/// </para>
/// </remarks>
public class SqlServerEmbeddingModelRegistry(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    SqlServerSearchIndexReferenceDataCache referenceDataCache,
    ILogger<SqlServerEmbeddingModelRegistry> logger)
{
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly SqlServerSearchIndexReferenceDataCache _referenceDataCache =
        referenceDataCache ?? throw new ArgumentNullException(nameof(referenceDataCache));
    private readonly ILogger<SqlServerEmbeddingModelRegistry> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Returns the <c>EmbeddingModelId</c> for <paramref name="modelKey"/>, creating the
    /// <c>dbo.EmbeddingModel</c> row on first use.
    /// </summary>
    /// <exception cref="SqlException">
    /// <paramref name="modelKey"/> already exists with a different <see cref="short"/> dimensions value
    /// (SQL error 50409 from <c>dbo.GetOrCreateEmbeddingModel</c>) -- two different embedding models must
    /// never share a <c>ModelKey</c>.
    /// </exception>
    public async Task<short> GetIdAsync(string modelKey, short dimensions, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelKey);

        var cachedId = _referenceDataCache.TryGetEmbeddingModelIdFromCache(modelKey);
        if (cachedId is { } id)
        {
            return id;
        }

        using var command = new SqlCommand(
            "EXEC dbo.GetOrCreateEmbeddingModel @ModelKey, @Dimensions, @EmbeddingModelId OUTPUT")
        {
            CommandType = CommandType.Text
        };
        command.Parameters.Add(new SqlParameter("@ModelKey", SqlDbType.VarChar, 256) { Value = modelKey });
        command.Parameters.Add(new SqlParameter("@Dimensions", SqlDbType.SmallInt) { Value = dimensions });
        var embeddingModelIdParameter = new SqlParameter("@EmbeddingModelId", SqlDbType.SmallInt)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(embeddingModelIdParameter);

        await _sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);

        var embeddingModelId = (short)embeddingModelIdParameter.Value!;
        _referenceDataCache.CacheEmbeddingModelId(modelKey, embeddingModelId);

        _logger.LogDebug(
            "Resolved EmbeddingModelId {EmbeddingModelId} for ModelKey {ModelKey} (TenantId={TenantId})",
            embeddingModelId,
            modelKey,
            tenantId);

        return embeddingModelId;
    }
}
