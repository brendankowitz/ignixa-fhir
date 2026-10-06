// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.SemanticSearch;

/// <summary>
/// Resolves a semantic embedding model's <c>EmbeddingModelId</c> via <c>dbo.GetOrCreateEmbeddingModel</c>,
/// caching the result in a process-wide, tenant-qualified <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a static, process-wide cache rather than an instance field.</b> This type is constructed per
/// request, the same way <c>SqlServerPostMergeExtensionUpdater</c> is (see
/// <c>SqlServerRepositoryFactory.CreateRepository</c>) -- not once per tenant for the process lifetime,
/// the way <c>SqlServerSearchIndexReferenceDataCache</c> is (see <c>SqlServerSearchIndexCacheRegistry</c>).
/// An instance-scoped cache would therefore be thrown away and rebuilt on every request, defeating the
/// point of caching a value -- <c>(TenantId, ModelKey) -&gt; EmbeddingModelId</c> -- that never changes once
/// created. A static dictionary keyed by <c>(TenantId, ModelKey)</c> gives every per-request instance,
/// across every tenant this process serves, the same long-lived cache that
/// <c>SqlServerSearchIndexReferenceDataCache</c> gets through its own tenant-scoped-singleton mechanism,
/// without this type needing a registry of its own. Unlike that cache, nothing ever invalidates an entry
/// here: a (TenantId, ModelKey) mapping is permanent once <c>dbo.GetOrCreateEmbeddingModel</c> creates it
/// (rows are never deleted, and a later call for the same key with different dimensions is a caller bug
/// the stored procedure rejects outright, not a legitimate remapping).
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
    ILogger<SqlServerEmbeddingModelRegistry> logger)
{
    private static readonly ConcurrentDictionary<(int TenantId, string ModelKey), short> Cache = new();

    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
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

        var cacheKey = (tenantId, modelKey);
        if (Cache.TryGetValue(cacheKey, out var cachedId))
        {
            return cachedId;
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
        Cache[cacheKey] = embeddingModelId;

        _logger.LogDebug(
            "Resolved EmbeddingModelId {EmbeddingModelId} for ModelKey {ModelKey} (TenantId={TenantId})",
            embeddingModelId,
            modelKey,
            tenantId);

        return embeddingModelId;
    }
}
