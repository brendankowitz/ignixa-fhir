using System.Data;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Raw-ADO.NET port of <c>SqlEntityFrameworkRepository</c> (Ignixa.DataLayer.SqlEntityFramework)
/// against the same legacy fhir-server schema, using <see cref="ISqlExecutionService"/> instead of
/// EF Core (design doc section 4: no ORM). Delegates bulk/index writes to
/// <see cref="SqlServerMergeRepository"/> exactly as the EF version delegates to
/// <c>SqlMergeRepository</c>.
///
/// Phase D Task 6 implements 5 of the 12 <see cref="IFhirRepository"/> members
/// (<see cref="GetAsync"/>, <see cref="CreateOrUpdateAsync"/>, <see cref="DeleteAsync"/>,
/// <see cref="GetNextTransactionIdAsync"/>, <see cref="CommitTransactionAsync"/>) plus the two shared
/// private helpers (<see cref="GetOrCreateResourceTypeIdAsync"/>, <see cref="GetNextSurrogateIdAsync"/>)
/// and <see cref="UpsertResourceTtlAsync"/>/<see cref="DeleteSearchIndexEntriesAsync"/>. The remaining
/// 7 members are intentionally unimplemented placeholders (Tasks 7-9 fill them in on this same class).
/// </summary>
public class SqlServerFhirRepository(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    GzipResourceCompressor compressor,
    SqlServerSearchIndexReferenceDataCache cache,
    SqlServerMergeRepository mergeRepository,
    ILogger<SqlServerFhirRepository> logger) : IFhirRepository
{
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly GzipResourceCompressor _compressor =
        compressor ?? throw new ArgumentNullException(nameof(compressor));
    private readonly SqlServerSearchIndexReferenceDataCache _cache =
        cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly SqlServerMergeRepository _mergeRepository =
        mergeRepository ?? throw new ArgumentNullException(nameof(mergeRepository));
    private readonly ILogger<SqlServerFhirRepository> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly int _tenantId = tenantId;

    private static readonly string[] SearchIndexTables =
    [
        "ReferenceSearchParam",
        "TokenSearchParam",
        "TokenText",
        "StringSearchParam",
        "UriSearchParam",
        "NumberSearchParam",
        "QuantitySearchParam",
        "DateTimeSearchParam",
        "ReferenceTokenCompositeSearchParam",
        "TokenTokenCompositeSearchParam",
        "TokenDateTimeCompositeSearchParam",
        "TokenQuantityCompositeSearchParam",
        "TokenStringCompositeSearchParam",
        "TokenNumberNumberCompositeSearchParam",
        "ResourceWriteClaim"
    ];

    /// <inheritdoc/>
    public async ValueTask<SearchEntryResult?> GetAsync(ResourceKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        _logger.LogDebug("Getting resource {ResourceType}/{ResourceId}", key.ResourceType, key.Id);

        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(key.ResourceType, ct);

        SqlCommand command;
        if (key.VersionId != null && int.TryParse(key.VersionId, out var version))
        {
            command = new SqlCommand(
                """
                SELECT r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, t.CreateDate
                FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
                WHERE r.ResourceTypeId = @ResourceTypeId AND r.ResourceId = @ResourceId AND r.Version = @Version;
                """);
            command.Parameters.Add("@Version", SqlDbType.Int).Value = version;
        }
        else
        {
            command = new SqlCommand(
                """
                SELECT TOP (1) r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, t.CreateDate
                FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
                WHERE r.ResourceTypeId = @ResourceTypeId AND r.ResourceId = @ResourceId AND r.IsHistory = 0
                ORDER BY r.Version DESC;
                """);
        }

        using (command)
        {
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = key.Id;

            var rows = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, ReadResourceRow, ct);

            if (rows.Count == 0)
            {
                _logger.LogDebug("Resource not found: {ResourceType}/{ResourceId}", key.ResourceType, key.Id);
                return null;
            }

            var row = rows[0];

            // NOTE: Do NOT filter out deleted resources here - return them with IsDeleted=true.
            // The API layer checks IsDeleted and returns 410 Gone (404 = never existed, 410 = deleted).
            var result = new SearchEntryResult(
                ResourceType: key.ResourceType,
                ResourceId: row.ResourceId,
                VersionId: row.Version.ToString(),
                LastModified: row.CreateDate ?? DateTimeOffset.UtcNow,
                ResourceBytes: _compressor.DecompressBytes(row.RawResource))
            {
                IsDeleted = row.IsDeleted,
                TenantId = key.TenantId,
            };

            _logger.LogDebug("Retrieved resource {ResourceType}/{ResourceId} version {Version}", key.ResourceType, key.Id, row.Version);

            return result;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<UpdateResult> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(resource.ResourceType);
        ArgumentException.ThrowIfNullOrEmpty(resource.ResourceId);

        if (resource.Resource == null)
        {
            throw new ArgumentException("Resource is required", nameof(resource));
        }

        _logger.LogDebug("Creating/updating resource {ResourceType}/{ResourceId}", resource.ResourceType, resource.ResourceId);

        var transactionId = await GetNextTransactionIdAsync(ct);

        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(resource.ResourceType, ct);

        var currentVersion = await GetCurrentVersionOrderedBySurrogateIdAsync(resourceTypeId, resource.ResourceId, ct);
        var newVersion = currentVersion.HasValue ? currentVersion.Value + 1 : 1;

        // Must happen BEFORE handing resource.Resource to the merge repository -- the merge path
        // compresses resource.Resource into RawResource bytes, so the version/timestamp needs to be
        // baked in first (matches legacy SqlEntityFrameworkRepository.cs:159-160 exactly).
        resource.Resource.Meta.VersionId = newVersion.ToString();
        resource.Resource.Meta.LastUpdated = transactionId.Value.ToDate();

        var resourceList = new[] { resource };
        var entryIndices = new[] { 0 };

        await _mergeRepository.MergeResourcesAsync(
            transactionId.Value,
            singleTransaction: true,
            resourceList,
            entryIndices,
            ct);

        await _mergeRepository.CommitTransactionAsync(
            transactionId: transactionId.Value,
            failureReason: null,
            cancellationToken: ct);

        await UpsertResourceTtlAsync(resourceTypeId, resource.ResourceId, resource.ExpiresAt, transactionId.Value, ct);

        _logger.LogInformation(
            "Created/updated resource {ResourceType}/{ResourceId} version {Version} via merge",
            resource.ResourceType, resource.ResourceId, newVersion);

        var compressedData = _compressor.SerializeAndCompress(resource.Resource);
        var lastModified = resource.Resource.Meta.LastUpdated ?? DateTimeOffset.UtcNow;

        var key = new ResourceKey(
            ResourceType: resource.ResourceType,
            Id: resource.ResourceId,
            VersionId: newVersion.ToString(),
            TenantId: resource.TenantId);

        return new UpdateResult(
            Key: key,
            ResourceBytes: _compressor.DecompressBytes(compressedData),
            LastModified: lastModified)
        {
            Request = resource.Request
        };
    }

    /// <inheritdoc/>
    public async ValueTask<ResourceKey?> DeleteAsync(
        ResourceKey key,
        ResourceRequest request,
        TransactionId? transactionId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Deleting resource {ResourceType}/{ResourceId}", key.ResourceType, key.Id);

        var resourceTypeId = await GetOrCreateResourceTypeIdAsync(key.ResourceType, ct);

        var currentEntity = await GetCurrentResourceForDeleteAsync(resourceTypeId, key.Id, ct);

        if (currentEntity == null)
        {
            _logger.LogWarning(
                "Cannot delete {ResourceType}/{ResourceId}: resource never existed", key.ResourceType, key.Id);
            return null;
        }

        if (currentEntity.Value.IsDeleted)
        {
            _logger.LogDebug(
                "Resource {ResourceType}/{ResourceId} already deleted at version {Version} (idempotent)",
                key.ResourceType, key.Id, currentEntity.Value.Version);

            return new ResourceKey(key.ResourceType, key.Id, currentEntity.Value.Version.ToString(), key.TenantId);
        }

        var newVersion = currentEntity.Value.Version + 1;

        // transactionId is this method's own OPTIONAL parameter (nullable) -- NOT a value obtained
        // via GetNextTransactionIdAsync(). No CommitTransactionAsync call anywhere in this method:
        // these statements run sequentially, uncommitted-as-a-unit, matching CLAUDE.md's documented
        // application-level-transaction philosophy.
        using (var historyCommand = new SqlCommand(
            "UPDATE dbo.Resource SET IsHistory=1, HistoryTransactionId=@HistoryTransactionId WHERE ResourceSurrogateId=@ResourceSurrogateId"))
        {
            historyCommand.Parameters.Add("@HistoryTransactionId", SqlDbType.BigInt).Value =
                (object?)transactionId?.Value ?? DBNull.Value;
            historyCommand.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = currentEntity.Value.ResourceSurrogateId;
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, historyCommand, ct);
        }

        var tombstoneJsonNode = new ResourceJsonNode
        {
            ResourceType = key.ResourceType,
            Id = key.Id,
            Meta = new MetaJsonNode
            {
                VersionId = newVersion.ToString(),
                LastUpdated = DateTimeOffset.UtcNow
            }
        };
        var compressedTombstone = _compressor.SerializeAndCompress(tombstoneJsonNode);

        var newSurrogateId = await GetNextSurrogateIdAsync(ct);

        using (var insertCommand = new SqlCommand(
            """
            INSERT INTO dbo.Resource
                (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId)
            VALUES
                (@ResourceTypeId, @ResourceId, @NewVersion, 0, @NewSurrogateId, 1, 'DELETE', @TombstoneBytes, 1, NULL, @TransactionId);
            """))
        {
            insertCommand.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            insertCommand.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = key.Id;
            insertCommand.Parameters.Add("@NewVersion", SqlDbType.Int).Value = newVersion;
            insertCommand.Parameters.Add("@NewSurrogateId", SqlDbType.BigInt).Value = newSurrogateId;
            insertCommand.Parameters.Add("@TombstoneBytes", SqlDbType.VarBinary).Value = compressedTombstone;
            insertCommand.Parameters.Add("@TransactionId", SqlDbType.BigInt).Value =
                (object?)transactionId?.Value ?? DBNull.Value;
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, insertCommand, ct);
        }

        await UpsertResourceTtlAsync(resourceTypeId, key.Id, expiresAt: null, transactionId?.Value, ct);

        await DeleteSearchIndexEntriesAsync(currentEntity.Value.ResourceSurrogateId, ct);

        _logger.LogInformation(
            "Created tombstone for {ResourceType}/{ResourceId} version {Version}", key.ResourceType, key.Id, newVersion);

        return new ResourceKey(key.ResourceType, key.Id, newVersion.ToString(), key.TenantId);
    }

    /// <inheritdoc/>
    public async ValueTask<TransactionId> GetNextTransactionIdAsync(CancellationToken ct = default)
    {
        var (id, _) = await _mergeRepository.BeginTransactionAsync(1000, ct);
        return new TransactionId(id);
    }

    /// <inheritdoc/>
    public async ValueTask CommitTransactionAsync(TransactionId transactionId, CancellationToken ct = default)
    {
        await _mergeRepository.CommitTransactionAsync(transactionId.Value, failureReason: null, ct);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(
        TransactionId transactionId,
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
        CancellationToken ct = default)
        => throw new NotImplementedException("BatchWriteAsync is implemented in a later Phase D task.");

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<TransactionId>> GetStalledTransactionsAsync(
        TimeSpan stallThreshold,
        CancellationToken ct = default)
        => throw new NotImplementedException("GetStalledTransactionsAsync is implemented in a later Phase D task.");

    /// <inheritdoc/>
    public IAsyncEnumerable<SearchEntryResult> GetResourceHistoryAsync(
        ResourceKey key,
        HistoryQueryParameters parameters,
        CancellationToken ct = default)
        => throw new NotImplementedException("GetResourceHistoryAsync is implemented in a later Phase D task.");

    /// <inheritdoc/>
    public IAsyncEnumerable<SearchEntryResult> GetTypeHistoryAsync(
        string resourceType,
        int tenantId,
        HistoryQueryParameters parameters,
        CancellationToken ct = default)
        => throw new NotImplementedException("GetTypeHistoryAsync is implemented in a later Phase D task.");

    /// <inheritdoc/>
    public IAsyncEnumerable<SearchEntryResult> GetSystemHistoryAsync(
        int tenantId,
        HistoryQueryParameters parameters,
        CancellationToken ct = default)
        => throw new NotImplementedException("GetSystemHistoryAsync is implemented in a later Phase D task.");

    /// <inheritdoc/>
    public Task<IReadOnlyList<ExpiredResourceInfo>> GetExpiredResourcesAsync(
        int batchSize,
        CancellationToken ct = default)
        => throw new NotImplementedException("GetExpiredResourcesAsync is implemented in a later Phase D task.");

    /// <inheritdoc/>
    public Task HardDeleteResourceAsync(
        short resourceTypeId,
        string resourceId,
        CancellationToken ct = default)
        => throw new NotImplementedException("HardDeleteResourceAsync is implemented in a later Phase D task.");

    // Corrected during plan review: an earlier draft routed the insert path through
    // _cache.GetResourceTypeIdAsync (the read-only lookup, which caches a "confirmed missing"
    // sentinel on a miss) and never updated the cache after inserting -- every subsequent call for
    // the same new type name would see the stale sentinel, conclude "still missing," and attempt to
    // insert AGAIN (a duplicate row / unique-constraint violation on the second caller).
    // CacheResourceTypeId records the freshly-inserted ID directly, bypassing the sentinel path.
    private async Task<short> GetOrCreateResourceTypeIdAsync(string resourceType, CancellationToken ct)
    {
        var cached = _cache.TryGetResourceTypeIdFromCache(resourceType);
        if (cached.HasValue)
        {
            return cached.Value;
        }

        var id = await _cache.GetResourceTypeIdAsync(resourceType, ct);
        if (id.HasValue)
        {
            return id.Value;
        }

        using var command = new SqlCommand(
            "INSERT INTO dbo.ResourceType (Name) OUTPUT INSERTED.ResourceTypeId VALUES (@Name)");
        command.Parameters.AddWithValue("@Name", resourceType);
        var results = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt16(0), ct);
        var newId = results[0];
        _cache.CacheResourceTypeId(resourceType, newId);
        return newId;
    }

    private async Task<long> GetNextSurrogateIdAsync(CancellationToken ct)
    {
        using var command = new SqlCommand("SELECT NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence");
        var results = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt32(0), ct);
        var sequenceValue = results[0];
        return (long)(DateTimeOffset.UtcNow - DateTimeOffset.MinValue).TotalMilliseconds * 80000 + sequenceValue;
    }

    private async Task UpsertResourceTtlAsync(
        short resourceTypeId,
        string resourceId,
        DateTimeOffset? expiresAt,
        long? transactionId,
        CancellationToken ct)
    {
        if (expiresAt.HasValue)
        {
            using var command = new SqlCommand(
                """
                MERGE dbo.ResourceTtl AS target
                USING (SELECT @ResourceTypeId AS ResourceTypeId, @ResourceId AS ResourceId) AS source
                ON target.ResourceTypeId = source.ResourceTypeId AND target.ResourceId = source.ResourceId
                WHEN MATCHED THEN UPDATE SET ExpiresAt = @ExpiresAt, TransactionId = @TransactionId
                WHEN NOT MATCHED THEN INSERT (ResourceTypeId, ResourceId, ExpiresAt, TransactionId) VALUES (@ResourceTypeId, @ResourceId, @ExpiresAt, @TransactionId);
                """);
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;
            command.Parameters.Add("@ExpiresAt", SqlDbType.DateTimeOffset).Value = expiresAt.Value;
            command.Parameters.Add("@TransactionId", SqlDbType.BigInt).Value = (object?)transactionId ?? DBNull.Value;
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, ct);
        }
        else
        {
            using var command = new SqlCommand(
                "DELETE FROM dbo.ResourceTtl WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId;");
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, ct);
        }
    }

    private async Task DeleteSearchIndexEntriesAsync(long resourceSurrogateId, CancellationToken ct)
    {
        var deleteStatements = string.Join(
            "\n",
            SearchIndexTables.Select(table => $"DELETE FROM dbo.{table} WHERE ResourceSurrogateId = @ResourceSurrogateId;"));

        // CA2100 suppressed: deleteStatements is built exclusively from the fixed, hardcoded
        // SearchIndexTables array above -- never from caller/user input -- matching the identical
        // rationale used by SqlEntityFrameworkRepository.DeleteSearchIndexEntriesAsync.
#pragma warning disable CA2100
        using var command = new SqlCommand(deleteStatements);
#pragma warning restore CA2100
        command.Parameters.Add("@ResourceSurrogateId", SqlDbType.BigInt).Value = resourceSurrogateId;
        await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, ct);

        _logger.LogDebug("Deleted search index entries for ResourceSurrogateId={ResourceSurrogateId}", resourceSurrogateId);
    }

    private async Task<int?> GetCurrentVersionOrderedBySurrogateIdAsync(short resourceTypeId, string resourceId, CancellationToken ct)
    {
        using var command = new SqlCommand(
            "SELECT TOP (1) Version FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId AND IsHistory = 0 ORDER BY ResourceSurrogateId DESC");
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;

        var rows = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt32(0), ct);
        return rows.Count == 0 ? null : rows[0];
    }

    private async Task<(long ResourceSurrogateId, int Version, bool IsDeleted)?> GetCurrentResourceForDeleteAsync(
        short resourceTypeId, string resourceId, CancellationToken ct)
    {
        using var command = new SqlCommand(
            "SELECT TOP (1) ResourceSurrogateId, Version, IsDeleted FROM dbo.Resource WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId AND IsHistory = 0 ORDER BY Version DESC");
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
        command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            reader => (ResourceSurrogateId: reader.GetInt64(0), Version: reader.GetInt32(1), IsDeleted: reader.GetBoolean(2)),
            ct);

        return rows.Count == 0 ? null : rows[0];
    }

    private static (string ResourceId, int Version, byte[] RawResource, bool IsDeleted, string? RequestMethod, DateTimeOffset? CreateDate) ReadResourceRow(SqlDataReader reader)
    {
        return (
            reader.GetString(0),
            reader.GetInt32(1),
            (byte[])reader[2],
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)));
    }
}
