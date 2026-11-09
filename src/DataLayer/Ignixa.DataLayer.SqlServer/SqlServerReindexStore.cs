using System.Data;
using System.Globalization;
using System.Text;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// SQL Server implementation of the barrier-fenced reindex storage contract.
/// </summary>
public sealed class SqlServerReindexStore(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    GzipResourceCompressor compressor,
    SqlServerSearchIndexReferenceDataCache referenceDataCache,
    SqlServerPostMergeExtensionUpdater extensionUpdater,
    ILogger logger) : IReindexStore
{
    /// <summary>The <c>dbo.Parameters</c> row that holds the barrier; merges compare their definitions event id with it.</summary>
    internal const string BarrierParameterId = "Conformance.MinAcceptedDefinitionsEventId";

    // A field, not the captured parameter: the row generators below are initialized from it too.
    private readonly SqlServerSearchIndexReferenceDataCache _referenceDataCache = referenceDataCache;

    private readonly SearchIndexRowSetBuilder _searchIndexRowSetBuilder = new(referenceDataCache);

    public async Task<BarrierCutoff> RaiseBarrierAsync(
        long targetEventId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetEventId);

        // IGNORE_DUP_KEY turns a racing insert into a no-op, so the monotonic update after it raises the
        // barrier whether this insert or a concurrent one created the row; the barrier never moves backwards.
        using var command = new SqlCommand(
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.Parameters WHERE Id = @BarrierId)
            BEGIN
                INSERT INTO dbo.Parameters (Id, Bigint) VALUES (@BarrierId, @TargetEventId);
            END;

            UPDATE dbo.Parameters
            SET Bigint = @TargetEventId
            WHERE Id = @BarrierId
              AND (Bigint IS NULL OR Bigint < @TargetEventId);

            ;WITH ResourceCutoffCandidates AS (
                SELECT MAX(resource.ResourceSurrogateId) AS CutoffValue
                FROM dbo.ResourceType AS resourceTypes
                OUTER APPLY (
                    SELECT TOP (1) ResourceSurrogateId
                    FROM dbo.Resource
                    WHERE ResourceTypeId = resourceTypes.ResourceTypeId
                    ORDER BY ResourceSurrogateId DESC
                ) AS resource
            )
            SELECT
                ISNULL((
                    SELECT TOP (1) SurrogateIdRangeFirstValue
                    FROM dbo.Transactions
                    ORDER BY SurrogateIdRangeFirstValue DESC), -1),
                ISNULL((
                    SELECT MAX(CutoffValue)
                    FROM (
                        SELECT TOP (1) SurrogateIdRangeLastValue AS CutoffValue
                        FROM dbo.Transactions
                        ORDER BY SurrogateIdRangeLastValue DESC
                        UNION ALL
                        SELECT CutoffValue FROM ResourceCutoffCandidates
                    ) AS CutoffCandidates), -1);
            """);
        command.Parameters.Add("@TargetEventId", SqlDbType.BigInt).Value = targetEventId;
        command.Parameters.Add("@BarrierId", SqlDbType.VarChar, 100).Value = BarrierParameterId;

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            static reader => new BarrierCutoff(reader.GetInt64(0), reader.GetInt64(1)),
            cancellationToken,
            SqlCommandIdempotency.NonIdempotent);

        return rows.Single();
    }

    public async Task<IncompleteTransaction?> GetOldestIncompleteTransactionAsync(
        long cutoffTransactionId,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            """
            SELECT TOP (1) SurrogateIdRangeFirstValue, CreateDate, HeartbeatDate
            FROM dbo.Transactions
            WHERE IsCompleted = 0
              AND SurrogateIdRangeFirstValue <= @CutoffTransactionId
            ORDER BY SurrogateIdRangeFirstValue;
            """);
        command.Parameters.Add("@CutoffTransactionId", SqlDbType.BigInt).Value = cutoffTransactionId;
        var rows = await sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            static reader => new IncompleteTransaction(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.GetDateTime(2)),
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<SurrogateIdRangePage> GetSurrogateIdRangesAsync(
        string resourceType,
        long startAfterSurrogateId,
        long upperBoundSurrogateId,
        int targetRangeSize,
        int maxRanges,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentOutOfRangeException.ThrowIfLessThan(startAfterSurrogateId, -1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetRangeSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRanges);

        // Types are added to dbo.ResourceType as any instance first writes them, so a miss in the preloaded
        // snapshot is checked against the table; only a type that was never written has no rows to reindex.
        var resourceTypeId = await _referenceDataCache.GetResourceTypeIdAsync(resourceType, cancellationToken);
        if (!resourceTypeId.HasValue || startAfterSurrogateId >= upperBoundSurrogateId)
        {
            return new SurrogateIdRangePage([], null);
        }

        // One ordered read of at most maxRanges full ranges plus one row: numbering the rows groups them into
        // ranges of targetRangeSize, and the extra row, if present, forms group maxRanges and means another page
        // follows. Ranges deliberately cover ID gaps and each starts right after the previous one ends, so the
        // caller can discard each page without losing the contiguous cutoff partition.
        using var command = new SqlCommand(
            """
            WITH Candidates AS (
                SELECT TOP (@MaxRows)
                       ResourceSurrogateId,
                       (ROW_NUMBER() OVER (ORDER BY ResourceSurrogateId) - 1) / @TargetRangeSize AS RangeIndex
                FROM dbo.Resource
                WHERE ResourceTypeId = @ResourceTypeId
                  AND IsHistory = 0
                  AND IsDeleted = 0
                  AND ResourceSurrogateId > @StartAfterSurrogateId
                  AND ResourceSurrogateId <= @UpperBoundSurrogateId
                ORDER BY ResourceSurrogateId
            )
            SELECT MIN(ResourceSurrogateId), MAX(ResourceSurrogateId), COUNT_BIG(*)
            FROM Candidates
            GROUP BY RangeIndex
            ORDER BY RangeIndex;
            """);
        command.Parameters.Add("@MaxRows", SqlDbType.BigInt).Value = (long)targetRangeSize * maxRanges + 1;
        command.Parameters.Add("@TargetRangeSize", SqlDbType.BigInt).Value = targetRangeSize;
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;
        command.Parameters.Add("@StartAfterSurrogateId", SqlDbType.BigInt).Value = startAfterSurrogateId;
        command.Parameters.Add("@UpperBoundSurrogateId", SqlDbType.BigInt).Value = upperBoundSurrogateId;
        var groups = await sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            static reader => (First: reader.GetInt64(0), Last: reader.GetInt64(1), Count: reader.GetInt64(2)),
            cancellationToken);

        var hasMore = groups.Count > maxRanges;
        var kept = hasMore ? groups.Take(maxRanges).ToArray() : groups.ToArray();
        var ranges = kept.Select((group, index) => new SurrogateIdRange(
                index > 0 ? kept[index - 1].Last + 1 : startAfterSurrogateId == -1 ? group.First : startAfterSurrogateId + 1,
                // The final page's last range reaches the upper bound, so the pages partition the whole cutoff set.
                !hasMore && index == kept.Length - 1 ? upperBoundSurrogateId : group.Last,
                group.Count))
            .ToArray();

        return new SurrogateIdRangePage(ranges, hasMore ? kept[^1].Last : null);
    }

    public async Task<IReadOnlyList<ReindexResource>> ReadRangeAsync(
        string resourceType,
        long afterSurrogateId,
        long endSurrogateId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        var resourceTypeId = await _referenceDataCache.GetResourceTypeIdAsync(resourceType, cancellationToken);
        if (!resourceTypeId.HasValue)
        {
            return [];
        }

        using var command = new SqlCommand(
            """
            SELECT TOP (@MaxCount)
                   r.ResourceId,
                   r.Version,
                   r.RawResource,
                   r.ResourceSurrogateId
            FROM dbo.Resource r
            WHERE r.ResourceTypeId = @ResourceTypeId
              AND r.IsHistory = 0
              AND r.IsDeleted = 0
              AND r.ResourceSurrogateId > @AfterSurrogateId
              AND r.ResourceSurrogateId <= @End
            ORDER BY r.ResourceSurrogateId;
            """);
        command.Parameters.Add("@MaxCount", SqlDbType.Int).Value = maxCount;
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;
        command.Parameters.Add("@AfterSurrogateId", SqlDbType.BigInt).Value = afterSurrogateId;
        command.Parameters.Add("@End", SqlDbType.BigInt).Value = endSurrogateId;

        // The write path reads only the identity, version and surrogate ID; the surrogate ID is also the
        // version's lastUpdated, as on every merge path.
        return await sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            reader =>
            {
                var resourceId = reader.GetString(0);
                var surrogateId = reader.GetInt64(3);
                return new ReindexResource(
                    new ResourceWrapper(
                        resourceType,
                        resourceId,
                        reader.GetInt32(1).ToString(CultureInfo.InvariantCulture),
                        surrogateId.ToDate(),
                        ResourceJsonNode.Parse(Encoding.UTF8.GetString(compressor.DecompressBytes(reader.GetFieldValue<byte[]>(2)).Span)),
                        new ResourceRequest("PUT", $"{resourceType}/{resourceId}")),
                    surrogateId);
            },
            cancellationToken);
    }

    public async Task<SearchIndexUpdateResult> UpdateSearchIndicesAsync(
        IReadOnlyList<ReindexResource> resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Count == 0)
        {
            return new SearchIndexUpdateResult(0, 0);
        }

        await _referenceDataCache.EnsureResourceTypesPreloadedAsync(cancellationToken);
        await _referenceDataCache.EnsureSearchParametersPreloadedAsync(cancellationToken);

        var resourceWrappers = resources.Select(resource => resource.Resource).ToArray();
        var resourceTypeIdMap = _referenceDataCache.ResourceTypeMappings;
        var searchParameterIdMap = _referenceDataCache.SearchParameterMappings;
        var resourceSurrogateIdMap = resources.ToDictionary(resource => resource.Resource, resource => resource.ResourceSurrogateId);

        var resourceRecords = CreateResourceRecords(resources, resourceTypeIdMap);
        var resourceWriteClaims = await ReadResourceWriteClaimsAsync(resources, cancellationToken);
        var searchIndexRowSets = _searchIndexRowSetBuilder.Build(
            resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, logger);

        var failedResources = new SqlParameter("@FailedResources", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var barrier = new SqlParameter("@Barrier", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.Output,
        };

        // The shared lock on the barrier row is held until commit, so RaiseBarrierAsync's update waits for this
        // write, and a write extracted from definitions below a raised barrier is skipped.
        using var command = new SqlCommand(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            SET @Barrier = ISNULL((SELECT Bigint FROM dbo.Parameters WITH (HOLDLOCK) WHERE Id = @BarrierId), 0);
            IF @Barrier <= @DefinitionsEventId
            EXEC dbo.UpdateResourceSearchParams
                @FailedResources OUTPUT,
                @Resources,
                @ResourceWriteClaims,
                @ReferenceSearchParams,
                @TokenSearchParams,
                @TokenTexts,
                @StringSearchParams,
                @UriSearchParams,
                @NumberSearchParams,
                @QuantitySearchParams,
                @DateTimeSearchParams,
                @ReferenceTokenCompositeSearchParams,
                @TokenTokenCompositeSearchParams,
                @TokenDateTimeCompositeSearchParams,
                @TokenQuantityCompositeSearchParams,
                @TokenStringCompositeSearchParams,
                @TokenNumberNumberCompositeSearchParams;
            COMMIT TRANSACTION;
            """);
        command.Parameters.Add(failedResources);
        command.Parameters.Add(barrier);
        command.Parameters.Add("@BarrierId", SqlDbType.VarChar, 100).Value = BarrierParameterId;
        var definitionsEventId = DefinitionsEventIdSelector.GetMinimum(resourceWrappers);
        command.Parameters.Add("@DefinitionsEventId", SqlDbType.BigInt).Value = definitionsEventId;
        SearchIndexRowSets.AddTableValuedParameter(command, "@Resources", "dbo.ResourceList", resourceRecords);
        SearchIndexRowSets.AddTableValuedParameter(command, "@ResourceWriteClaims", "dbo.ResourceWriteClaimList", resourceWriteClaims);
        searchIndexRowSets.AddTableValuedParameters(command);

        try
        {
            var updatedResourceSurrogateIds = await sqlExecutionService.ExecuteReaderAsync(
                tenantId,
                command,
                static reader => reader.GetInt64(0),
                cancellationToken,
                SqlCommandIdempotency.Idempotent);
            if ((long)barrier.Value > definitionsEventId)
            {
                return SearchIndexUpdateResult.Refused((long)barrier.Value);
            }

            // Only resources the procedure actually updated; a conflicted (superseded) resource keeps its rows.
            var updatedResourceSurrogateIdSet = updatedResourceSurrogateIds.ToHashSet();
            var conflicts = Convert.ToInt32(failedResources.Value, CultureInfo.InvariantCulture);
            var tokenExtensions = _searchIndexRowSetBuilder.TokenRowGenerator.ExtractExtensionData(
                    resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, logger)
                .Where(extension => updatedResourceSurrogateIdSet.Contains(extension.ResourceSurrogateId))
                .ToArray();
            var uriExtensions = _searchIndexRowSetBuilder.UriRowGenerator.ExtractExtensionData(
                    resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, logger)
                .Where(extension => updatedResourceSurrogateIdSet.Contains(extension.ResourceSurrogateId))
                .ToArray();
            if (tokenExtensions.Length > 0 || uriExtensions.Length > 0)
            {
                try
                {
                    await extensionUpdater.UpdateAllExtensionsAsync(tokenExtensions, uriExtensions, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to update extension columns after reindex (TenantId={TenantId}, ResourceCount={ResourceCount}, TokenExtensionCount={TokenExtensionCount}, UriExtensionCount={UriExtensionCount}). Core search indices were successfully updated; extension columns remain NULL.",
                        tenantId,
                        resources.Count,
                        tokenExtensions.Length,
                        uriExtensions.Length);
                }
            }

            return new SearchIndexUpdateResult(resources.Count - conflicts, conflicts);
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            throw new TimeoutException("The SQL reindex write timed out.", ex);
        }
    }

    // Resolved through the same catalog cache extraction and search use, so the answer is the one they see.
    public async Task<bool> HasSearchParameterAsync(
        string storageCanonical,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(storageCanonical);
        return await _referenceDataCache.GetSearchParamIdAsync(storageCanonical, cancellationToken) is not null;
    }

    private async Task<IList<SqlDataRecord>?> ReadResourceWriteClaimsAsync(
        IReadOnlyList<ReindexResource> resources,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            """
            SELECT claims.ResourceSurrogateId, claims.ClaimTypeId, claims.ClaimValue
            FROM dbo.ResourceWriteClaim AS claims
            INNER JOIN OPENJSON(@ResourceSurrogateIds) AS resourceIds
                ON claims.ResourceSurrogateId = CONVERT(BIGINT, resourceIds.[value]);
            """);
        command.Parameters.Add("@ResourceSurrogateIds", SqlDbType.NVarChar, -1).Value =
            System.Text.Json.JsonSerializer.Serialize(resources.Select(resource => resource.ResourceSurrogateId));
        var claims = await sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            static reader => (
                ResourceSurrogateId: reader.GetInt64(0),
                ClaimTypeId: reader.GetByte(1),
                ClaimValue: reader.GetString(2)),
            cancellationToken);

        if (claims.Count == 0)
        {
            return null;
        }

        SqlMetaData[] metadata =
        [
            new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
            new SqlMetaData("ClaimTypeId", SqlDbType.TinyInt),
            new SqlMetaData("ClaimValue", SqlDbType.NVarChar, 128),
        ];
        var records = new List<SqlDataRecord>(claims.Count);
        foreach (var claim in claims)
        {
            var record = new SqlDataRecord(metadata);
            record.SetInt64(0, claim.ResourceSurrogateId);
            record.SetByte(1, claim.ClaimTypeId);
            record.SetString(2, claim.ClaimValue);
            records.Add(record);
        }

        return records;
    }

    private static IList<SqlDataRecord> CreateResourceRecords(
        IReadOnlyList<ReindexResource> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap)
    {
        SqlMetaData[] metadata =
        [
            new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
            new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
            new SqlMetaData("ResourceId", SqlDbType.VarChar, 64),
            new SqlMetaData("Version", SqlDbType.Int),
            new SqlMetaData("HasVersionToCompare", SqlDbType.Bit),
            new SqlMetaData("IsDeleted", SqlDbType.Bit),
            new SqlMetaData("IsHistory", SqlDbType.Bit),
            new SqlMetaData("KeepHistory", SqlDbType.Bit),
            new SqlMetaData("RawResource", SqlDbType.VarBinary, -1),
            new SqlMetaData("IsRawResourceMetaSet", SqlDbType.Bit),
            new SqlMetaData("RequestMethod", SqlDbType.VarChar, 10),
            new SqlMetaData("SearchParamHash", SqlDbType.VarChar, 64),
        ];
        var records = new List<SqlDataRecord>(resources.Count);
        foreach (var reindexResource in resources)
        {
            var resource = reindexResource.Resource;
            if (!resourceTypeIdMap.TryGetValue(resource.ResourceType, out var resourceTypeId))
            {
                throw new InvalidOperationException($"Resource type '{resource.ResourceType}' is not present in dbo.ResourceType.");
            }

            var record = new SqlDataRecord(metadata);
            record.SetInt16(0, resourceTypeId);
            record.SetInt64(1, reindexResource.ResourceSurrogateId);
            record.SetString(2, resource.ResourceId);
            record.SetInt32(3, int.Parse(resource.VersionId, CultureInfo.InvariantCulture));
            record.SetBoolean(4, false);
            record.SetBoolean(5, false);
            record.SetBoolean(6, false);
            record.SetBoolean(7, false);
            record.SetBytes(8, 0, [], 0, 0);
            record.SetBoolean(9, false);
            record.SetString(10, resource.Request.Method);
            record.SetDBNull(11);
            records.Add(record);
        }

        return records;
    }
}
