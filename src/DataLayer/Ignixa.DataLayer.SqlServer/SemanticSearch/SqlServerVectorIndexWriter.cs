// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Security.Cryptography;
using System.Text;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Models;
using Ignixa.Search.Sql.Builders;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.SemanticSearch;

/// <summary>
/// Persists <see cref="ResourceWrapper.VectorIndices"/> to <c>dbo.VectorSearchParam</c> via
/// <c>dbo.MergeVectorSearchParams</c>, strictly AFTER <c>dbo.MergeResources</c> has already committed the
/// write these vectors belong to -- see that procedure's header (<c>MergeVectorSearchParams.sql</c>) for
/// the lock-ordering race this split avoids. Called from <c>SqlServerMergeRepository.MergeResourcesAsync</c>,
/// which is solely responsible for catching and logging (never rethrowing) any failure from
/// <see cref="WriteAsync"/>: a vector-persistence failure must never fail an already-committed resource
/// write (semantic-search-slice-1 plan, Global Constraints).
/// </summary>
public class SqlServerVectorIndexWriter(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    GzipResourceCompressor compressor,
    SqlServerSearchIndexReferenceDataCache referenceDataCache,
    SqlServerEmbeddingModelRegistry embeddingModelRegistry,
    ILogger<SqlServerVectorIndexWriter> logger)
{
    /// <summary>Dimensions are fixed for this slice (ADR-2610 / plan Global Constraints).</summary>
    private const short EmbeddingDimensions = 1536;

    /// <summary>Matches <c>SqlServerPostMergeExtensionUpdater.BatchSize</c>.</summary>
    private const int BatchSize = 100;

    private static readonly SqlMetaData[] EvaluatedMetadata =
    [
        new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
        new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
    ];

    private static readonly SqlMetaData[] VectorMetadata =
    [
        new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
        new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
        new SqlMetaData("SearchParamId", SqlDbType.SmallInt),
        new SqlMetaData("EmbeddingModelId", SqlDbType.SmallInt),
        new SqlMetaData("ChunkOrdinal", SqlDbType.SmallInt),
        new SqlMetaData("SourceTextCompressed", SqlDbType.VarBinary, -1),
        new SqlMetaData("SourceTextHash", SqlDbType.Binary, 32),
        new SqlMetaData("Embedding", SqlDbType.NVarChar, -1),
    ];

    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly GzipResourceCompressor _compressor =
        compressor ?? throw new ArgumentNullException(nameof(compressor));
    private readonly SqlServerSearchIndexReferenceDataCache _referenceDataCache =
        referenceDataCache ?? throw new ArgumentNullException(nameof(referenceDataCache));
    private readonly SqlServerEmbeddingModelRegistry _embeddingModelRegistry =
        embeddingModelRegistry ?? throw new ArgumentNullException(nameof(embeddingModelRegistry));
    private readonly ILogger<SqlServerVectorIndexWriter> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Writes every evaluated resource's vectors, <see cref="BatchSize"/> resources per
    /// <c>dbo.MergeVectorSearchParams</c> call. An evaluated resource with zero <c>Entries</c> still
    /// belongs in its batch's <c>@Evaluated</c> rows -- the procedure deletes that resource's prior
    /// vectors on sight, which is exactly what "semantic text was removed on update" requires (evaluated
    /// empty is not the same as not evaluated at all; a <c>null</c> <see cref="ResourceWrapper.VectorIndices"/>
    /// never reaches this method -- the caller filters those out before building <paramref name="evaluated"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An entry's <see cref="VectorIndexEntry.SearchParameterUrl"/> has no <c>SearchParamId</c> in the
    /// tenant's search-parameter mappings. Thrown rather than silently dropping the row -- the caller
    /// (<c>SqlServerMergeRepository</c>) is the single place that catches and logs this without rethrowing.
    /// </exception>
    public async Task WriteAsync(
        IReadOnlyList<(short ResourceTypeId, long ResourceSurrogateId, IReadOnlyList<VectorIndexEntry> Entries)> evaluated,
        CancellationToken cancellationToken)
    {
        if (evaluated.Count == 0)
        {
            return;
        }

        var searchParameterIdMap = _referenceDataCache.SearchParameterMappings;

        foreach (var batch in evaluated.Chunk(BatchSize))
        {
            await WriteBatchAsync(batch, searchParameterIdMap, cancellationToken);
        }
    }

    private async Task WriteBatchAsync(
        IReadOnlyList<(short ResourceTypeId, long ResourceSurrogateId, IReadOnlyList<VectorIndexEntry> Entries)> batch,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        CancellationToken cancellationToken)
    {
        var evaluatedRecords = BuildEvaluatedRecords(batch);
        var vectorRecords = await BuildVectorRecordsAsync(batch, searchParameterIdMap, cancellationToken);

        try
        {
            await ExecuteMergeAsync(evaluatedRecords, vectorRecords, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            // MergeVectorSearchParams.sql's header documents this contract: a 1205 (deadlock) victim has
            // made no partial writes -- XACT_ABORT rolled it back whole -- so retrying once with the SAME
            // rows is safe. A second 1205 means a concurrent hard delete is in flight for one of these
            // resources and is the transaction that should win; propagate to the caller, which logs and
            // does not rethrow (never fail the already-committed resource write).
            _logger.LogWarning(
                ex, "dbo.MergeVectorSearchParams deadlocked for tenant {TenantId}; retrying once", tenantId);
            await ExecuteMergeAsync(evaluatedRecords, vectorRecords, cancellationToken);
        }
    }

    private async Task ExecuteMergeAsync(
        IList<SqlDataRecord> evaluatedRecords,
        IList<SqlDataRecord>? vectorRecords,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("dbo.MergeVectorSearchParams") { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@Evaluated", SqlDbType.Structured)
        {
            TypeName = "dbo.VectorResourceList",
            Value = evaluatedRecords,
        });
        command.Parameters.Add(new SqlParameter("@Vectors", SqlDbType.Structured)
        {
            TypeName = "dbo.VectorSearchParamList",
            // SqlClient requires NULL (not an empty list) for a TVP carrying zero rows.
            Value = vectorRecords,
        });

        await _sqlExecutionService.ExecuteNonQueryAsync(
            tenantId, command, cancellationToken, SqlCommandIdempotency.NonIdempotent);
    }

    private static IList<SqlDataRecord> BuildEvaluatedRecords(
        IReadOnlyList<(short ResourceTypeId, long ResourceSurrogateId, IReadOnlyList<VectorIndexEntry> Entries)> batch)
    {
        var records = new List<SqlDataRecord>(batch.Count);
        foreach (var (resourceTypeId, resourceSurrogateId, _) in batch)
        {
            var record = new SqlDataRecord(EvaluatedMetadata);
            record.SetInt16(0, resourceTypeId);
            record.SetInt64(1, resourceSurrogateId);
            records.Add(record);
        }
        return records;
    }

    private async Task<IList<SqlDataRecord>?> BuildVectorRecordsAsync(
        IReadOnlyList<(short ResourceTypeId, long ResourceSurrogateId, IReadOnlyList<VectorIndexEntry> Entries)> batch,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        CancellationToken cancellationToken)
    {
        List<SqlDataRecord>? records = null;

        foreach (var (resourceTypeId, resourceSurrogateId, entries) in batch)
        {
            foreach (var entry in entries)
            {
                if (!searchParameterIdMap.TryGetValue(entry.SearchParameterUrl.ToString(), out var searchParamId))
                {
                    throw new InvalidOperationException(
                        $"Semantic search parameter '{entry.SearchParameterUrl}' has no SearchParamId mapping; " +
                        $"vectors for ResourceTypeId={resourceTypeId}, ResourceSurrogateId={resourceSurrogateId} cannot be written.");
                }

                var embeddingModelId = await _embeddingModelRegistry.GetIdAsync(
                    entry.EmbeddingModelKey, EmbeddingDimensions, cancellationToken);

                foreach (var chunk in entry.Chunks)
                {
                    records ??= [];
                    records.Add(BuildVectorRecord(resourceTypeId, resourceSurrogateId, searchParamId, embeddingModelId, chunk));
                }
            }
        }

        return records;
    }

    private SqlDataRecord BuildVectorRecord(
        short resourceTypeId, long resourceSurrogateId, short searchParamId, short embeddingModelId, VectorChunk chunk)
    {
        var record = new SqlDataRecord(VectorMetadata);
        record.SetInt16(0, resourceTypeId);
        record.SetInt64(1, resourceSurrogateId);
        record.SetInt16(2, searchParamId);
        record.SetInt16(3, embeddingModelId);
        record.SetInt16(4, chunk.Ordinal);

        var compressed = _compressor.CompressText(chunk.Passage);
        record.SetBytes(5, 0, compressed, 0, compressed.Length);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(chunk.Passage));
        record.SetBytes(6, 0, hash, 0, hash.Length);

        record.SetString(7, SqlVectorText.Format(chunk.Embedding.Span));

        return record;
    }
}
