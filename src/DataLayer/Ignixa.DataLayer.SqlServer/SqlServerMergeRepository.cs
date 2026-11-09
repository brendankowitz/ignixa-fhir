using System.Data;
using System.Text.Json;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Ignixa.DataLayer.SqlServer.SemanticSearch;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// ADO.NET port of the write-path surface of <c>SqlMergeRepository</c>
/// (Ignixa.DataLayer.SqlEntityFramework). Ports the TVP-based bulk merge mechanism to
/// <see cref="ISqlExecutionService"/>: every EF <c>ExecuteSqlRawAsync("EXEC dbo.XXX ...")</c> call
/// becomes a <see cref="SqlCommand"/> executed via <see cref="ISqlExecutionService.ExecuteNonQueryAsync"/>,
/// with output parameter values read back off the same <see cref="SqlCommand.Parameters"/> collection.
/// <see cref="MergeResourcesAsync"/> also drives the post-merge extension-column update
/// (<see cref="SqlServerPostMergeExtensionUpdater"/>) internally, exactly as the original does --
/// extension columns (IdentifierType*, Version, Fragment) are never writable through the TVPs
/// themselves (CLAUDE.md's documented PostMergeExtensionUpdater pattern).
/// </summary>
public class SqlServerMergeRepository(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    GzipResourceCompressor compressor,
    SqlServerSearchIndexReferenceDataCache referenceDataCache,
    SqlServerPostMergeExtensionUpdater extensionUpdater,
    ILogger<SqlServerMergeRepository> logger,
    SqlServerVectorIndexWriter? vectorIndexWriter = null)
{
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly SqlServerSearchIndexReferenceDataCache _referenceDataCache =
        referenceDataCache ?? throw new ArgumentNullException(nameof(referenceDataCache));
    private readonly SqlServerPostMergeExtensionUpdater _extensionUpdater =
        extensionUpdater ?? throw new ArgumentNullException(nameof(extensionUpdater));
    private readonly ILogger<SqlServerMergeRepository> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SqlServerVectorIndexWriter? _vectorIndexWriter = vectorIndexWriter;

    private readonly ResourceRowGenerator _resourceRowGenerator =
        new(compressor ?? throw new ArgumentNullException(nameof(compressor)));
    private readonly ResourceWriteClaimRowGenerator _resourceWriteClaimRowGenerator = new();
    private readonly SearchIndexRowSetBuilder _searchIndexRowSetBuilder = new(referenceDataCache);

    /// <summary>
    /// Begins a merge transaction, allocating transaction ID and sequence range.
    /// </summary>
    /// <param name="resourceCount">Number of resources to be merged in this transaction.</param>
    /// <param name="definitionsEventId">Conformance position of the definitions used to extract indexes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing (TransactionId, SequenceRangeFirstValue).</returns>
    public async Task<(long TransactionId, int SequenceStart)> BeginTransactionAsync(
        int resourceCount,
        long definitionsEventId,
        CancellationToken cancellationToken = default)
    {
        // A full-cycle request cannot advance the procedure's wrap-retry loop.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resourceCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(resourceCount, 80000);
        ArgumentOutOfRangeException.ThrowIfNegative(definitionsEventId);

        _logger.LogDebug("Beginning merge transaction for {ResourceCount} resources", resourceCount);

        var transactionIdParam = new SqlParameter
        {
            ParameterName = "@TransactionId",
            SqlDbType = SqlDbType.BigInt,
            Direction = ParameterDirection.Output
        };

        var sequenceStartParam = new SqlParameter
        {
            ParameterName = "@SequenceRangeFirstValue",
            SqlDbType = SqlDbType.Int,
            Direction = ParameterDirection.Output
        };

        using var command = new SqlCommand(
            """
            EXEC dbo.MergeResourcesBeginTransaction
                @Count,
                @TransactionId OUTPUT,
                @SequenceRangeFirstValue OUTPUT,
                @HeartbeatDate;
            SELECT Bigint
            FROM dbo.Parameters
            WHERE Id = @BarrierId;
            """)
        {
            CommandType = CommandType.Text
        };
        command.Parameters.Add(new SqlParameter("@Count", SqlDbType.Int) { Value = resourceCount });
        command.Parameters.Add(transactionIdParam);
        command.Parameters.Add(sequenceStartParam);
        command.Parameters.Add(new SqlParameter("@HeartbeatDate", SqlDbType.DateTime) { Value = DBNull.Value });
        command.Parameters.Add(new SqlParameter("@BarrierId", SqlDbType.VarChar, 100) { Value = SqlServerReindexStore.BarrierParameterId });

        try
        {
            var barrierValues = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId,
                command,
                reader => reader.IsDBNull(0) ? 0L : reader.GetInt64(0),
                cancellationToken,
                SqlCommandIdempotency.NonIdempotent);
            var barrier = barrierValues.Count == 0 ? 0 : barrierValues[0];
            var transactionId = (long)transactionIdParam.Value!;
            var sequenceStart = (int)sequenceStartParam.Value!;

            if (definitionsEventId < barrier)
            {
                var staleException = new StaleConformanceDefinitionsException(
                    transactionId,
                    definitionsEventId,
                    barrier);
                await CommitTransactionAsync(
                    transactionId,
                    staleException.Message,
                    CancellationToken.None);
                throw staleException;
            }

            _logger.LogInformation(
                "Merge transaction started: TransactionId={TransactionId}, SequenceStart={SequenceStart}",
                transactionId,
                sequenceStart);

            return (transactionId, sequenceStart);
        }
        catch (Exception ex) when (ex is not StaleConformanceDefinitionsException)
        {
            _logger.LogError(ex,
                "Merge transaction allocation failed with an uncertain outcome for tenant {TenantId}; allocation was not replayed and requires transaction reconciliation",
                tenantId);
            throw;
        }
    }

    /// <summary>
    /// Merges a batch of resources using stored procedure with TVPs.
    /// </summary>
    /// <param name="transactionId">The transaction ID from BeginTransactionAsync.</param>
    /// <param name="singleTransaction">Whether this is a single atomic transaction.</param>
    /// <param name="resources">The resources to merge.</param>
    /// <param name="entryIndices">Bundle entry indices for surrogate ID calculation (transactionId + entryIndex).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of affected rows.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="singleTransaction"/> is <see langword="false"/> and at least one resource has a
    /// non-null <see cref="ResourceWrapper.VectorIndices"/>. See the remarks below on why that combination
    /// can never be served correctly, not just inefficiently.
    /// </exception>
    /// <remarks>
    /// <see cref="SqlServerVectorIndexWriter"/> persists vectors in a separate call that must run strictly
    /// AFTER this method's own <c>dbo.MergeResources</c> call has committed -- see that writer's remarks and
    /// <c>MergeVectorSearchParams.sql</c>'s header for the lock-ordering race this split avoids. That
    /// precondition holds today only because every production caller passes <paramref name="singleTransaction"/>
    /// <see langword="true"/>, under which this method commits the core merge before returning (see the
    /// commit call near the end of this method's body). Nothing enforces that at the call site, so a future
    /// caller passing <see langword="false"/> for a resource batch carrying vectors would silently run the
    /// vector writer against rows that are not yet, and might never be, committed -- exactly the
    /// commit-ordering violation the split exists to prevent. This method fails fast instead, before the
    /// merge call runs at all, so nothing is committed under the combination it cannot support.
    /// </remarks>
    public async Task<int> MergeResourcesAsync(
        long transactionId,
        bool singleTransaction,
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyList<int> entryIndices,
        CancellationToken cancellationToken = default)
    {
        if (resources == null || resources.Count == 0)
        {
            return 0;
        }

        // Validate that entryIndices matches resources count
        if (entryIndices == null || entryIndices.Count != resources.Count)
        {
            throw new ArgumentException(
                $"Entry indices count ({entryIndices?.Count ?? 0}) must match resources count ({resources.Count})",
                nameof(entryIndices));
        }

        // Programmer error, caught before anything is committed (AGENTS.md: fail fast). The vector writer
        // below only runs after this method's own merge call has committed, which this method guarantees
        // only when singleTransaction is true (see this method's remarks). A caller passing false for a
        // batch that includes vector-bearing resources has no commit point for the writer to run after, so
        // there is no correct behavior to fall back to -- not "skip the vectors", which would silently
        // index differently than the caller asked for.
        if (!singleTransaction && resources.Any(resource => resource.VectorIndices is not null))
        {
            throw new InvalidOperationException(
                "Semantic vector persistence requires singleTransaction: true. SqlServerVectorIndexWriter " +
                "runs only after MergeResources has committed, and a multi-transaction merge (singleTransaction: " +
                "false) has no commit point for it to run after -- the caller must either merge these resources " +
                "with singleTransaction: true, or not evaluate VectorIndices for a multi-transaction merge at all.");
        }

        _logger.LogDebug(
            "Merging {ResourceCount} resources for transaction {TransactionId}",
            resources.Count,
            transactionId);

        // Ensure cache is fully and safely preloaded before reading from it below. A bare
        // Count == 0 check here (the previous code) raced against concurrent callers on a cold
        // cache and could silently read a partially-populated dictionary -- see
        // docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md.
        // EnsureResourceTypesPreloadedAsync/EnsureSearchParametersPreloadedAsync are race-free.
        await _referenceDataCache.EnsureResourceTypesPreloadedAsync(cancellationToken);
        await _referenceDataCache.EnsureSearchParametersPreloadedAsync(cancellationToken);

        // Access cache dictionaries directly (no method call overhead)
        var resourceTypeIdMap = _referenceDataCache.ResourceTypeMappings;
        var searchParameterIdMap = _referenceDataCache.SearchParameterMappings;
        var systemIdMap = _referenceDataCache.SystemMappings;
        var quantityCodeIdMap = _referenceDataCache.QuantityCodeMappings;

        _logger.LogDebug(
            "Using reference data mappings: {ResourceTypes} resource types, {SearchParams} search params, {Systems} systems, {QuantityCodes} quantity codes",
            resourceTypeIdMap.Count,
            searchParameterIdMap.Count,
            systemIdMap.Count,
            quantityCodeIdMap.Count);

        // Build surrogate ID map (transactionId + entryIndex for each resource)
        var resourceSurrogateIdMap = BuildResourceSurrogateIdMap(transactionId, resources, entryIndices);

        // Generate TVP parameters using row generators
        // Resource TVP is always required (never null), so materialize to List directly
        var resourceRecords = _resourceRowGenerator.GenerateSqlDataRecords(transactionId, resources, resourceTypeIdMap, entryIndices).ToList();
        var resourceWriteClaimRecords = MaterializeIfNotEmpty(_resourceWriteClaimRowGenerator.GenerateSqlDataRecords(resources, resourceSurrogateIdMap));

        // Each set is materialized and NULL when empty - SQL Client requires NULL (not empty) for TVPs
        var searchIndexRowSets = _searchIndexRowSetBuilder.Build(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger);
        _logger.LogInformation("ReferenceSearchParams count: {Count}", searchIndexRowSets.Reference?.Count ?? 0);
        _logger.LogInformation("TokenSearchParams count: {Count}", searchIndexRowSets.Token?.Count ?? 0);

        // Create stored procedure parameters
        // NOTE: Using SqlDataRecord streaming (proper TVP pattern) instead of DataTable
        // SqlDataRecord provides better performance and guaranteed column ordering
        // IMPORTANT: Parameter order must match stored procedure signature exactly

        var affectedRowsParam = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };

        const string MergeCommandText =
            "EXEC dbo.MergeResources @AffectedRows OUTPUT, @RaiseExceptionOnConflict, @IsResourceChangeCaptureEnabled, " +
            "@TransactionId, @SingleTransaction, @Resources, @ResourceWriteClaims, " +
            "@ReferenceSearchParams, @TokenSearchParams, @TokenTexts, @StringSearchParams, @UriSearchParams, " +
            "@NumberSearchParams, @QuantitySearchParams, @DateTimeSearchParams, " +
            "@ReferenceTokenCompositeSearchParams, @TokenTokenCompositeSearchParams, " +
            "@TokenDateTimeCompositeSearchParams, @TokenQuantityCompositeSearchParams, @TokenStringCompositeSearchParams, " +
            "@TokenNumberNumberCompositeSearchParams";
        using var command = new SqlCommand(MergeCommandText)
        {
            CommandType = CommandType.Text
        };

        command.Parameters.Add(affectedRowsParam);
        command.Parameters.Add(new SqlParameter("@RaiseExceptionOnConflict", SqlDbType.Bit) { Value = true });
        command.Parameters.Add(new SqlParameter("@IsResourceChangeCaptureEnabled", SqlDbType.Bit) { Value = false });
        command.Parameters.Add(new SqlParameter("@TransactionId", SqlDbType.BigInt) { Value = transactionId });
        command.Parameters.Add(new SqlParameter("@SingleTransaction", SqlDbType.Bit) { Value = singleTransaction });
        command.Parameters.Add(new SqlParameter("@Resources", SqlDbType.Structured)
        {
            TypeName = "dbo.ResourceList",
            Value = resourceRecords
        });
        command.Parameters.Add(new SqlParameter("@ResourceWriteClaims", SqlDbType.Structured)
        {
            TypeName = "dbo.ResourceWriteClaimList",
            Value = resourceWriteClaimRecords
        });
        searchIndexRowSets.AddTableValuedParameters(command);

        // Extract extension data before calling the SP (needed for post-merge update)
        var tokenExtensions = _searchIndexRowSetBuilder.TokenRowGenerator.ExtractExtensionData(
            resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToList();
        var uriExtensions = _searchIndexRowSetBuilder.UriRowGenerator.ExtractExtensionData(
            resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToList();

        var expectedVersions = resources.Where(r => r.ExpectedVersionId != null)
            .Select(r => new
            {
                ResourceTypeId = resourceTypeIdMap[r.ResourceType],
                r.ResourceId,
                Version = int.Parse(r.ExpectedVersionId!, System.Globalization.CultureInfo.InvariantCulture),
                ExpiresAt = r.IsDeleted ? null : r.ExpiresAt
            }).ToArray();
        if (expectedVersions.Length > 0)
        {
            // Only the core command is enclosed: the committed core remains separate from the
            // post-merge extension updater below. HOLDLOCK also protects expected absence.
            command.CommandText = """
                SET XACT_ABORT ON;
                BEGIN TRY
                  BEGIN TRANSACTION;
                  IF EXISTS (
                    SELECT 1
                    FROM OPENJSON(@ExpectedVersions)
                    WITH (ResourceTypeId smallint, ResourceId varchar(64), Version int) expected
                    LEFT JOIN dbo.Resource currentResource WITH (UPDLOCK, HOLDLOCK)
                      ON currentResource.ResourceTypeId = expected.ResourceTypeId
                      AND currentResource.ResourceId = expected.ResourceId
                      AND currentResource.IsHistory = 0
                    WHERE ISNULL(currentResource.Version, 0) <> expected.Version
                  )
                    THROW 50412, 'The supplied resource version is not current.', 1;
                """ + MergeCommandText + ";" + """
                  MERGE dbo.ResourceTtl AS target
                  USING (
                    SELECT ResourceTypeId, ResourceId, ExpiresAt FROM OPENJSON(@ExpectedVersions)
                    WITH (ResourceTypeId smallint, ResourceId varchar(64), ExpiresAt datetimeoffset)
                  ) AS source
                  ON target.ResourceTypeId = source.ResourceTypeId AND target.ResourceId = source.ResourceId
                  WHEN MATCHED AND source.ExpiresAt IS NULL THEN DELETE
                  WHEN MATCHED THEN UPDATE SET ExpiresAt = source.ExpiresAt, TransactionId = @TransactionId
                  WHEN NOT MATCHED AND source.ExpiresAt IS NOT NULL THEN
                    INSERT (ResourceTypeId, ResourceId, ExpiresAt, TransactionId)
                    VALUES (source.ResourceTypeId, source.ResourceId, source.ExpiresAt, @TransactionId);
                  COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                  THROW;
                END CATCH;
                """;
            command.Parameters.Add("@ExpectedVersions", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(expectedVersions);
        }

        try
        {
            // Execute merge stored procedure
            await _sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken,
                expectedVersions.Length == 0 ? SqlCommandIdempotency.Idempotent : SqlCommandIdempotency.NonIdempotent);
        }
        catch (SqlException ex) when (ex.Number is 50409 or 50412 or 2601 or 2627)
        {
            // SQL error 50409: Resource has been recently updated or added (version conflict)
            throw new PreconditionFailedException("Resource was recently updated. Please refresh and retry.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Core merge {TransactionId} failed; a lost response may require transaction reconciliation",
                transactionId);
            throw;
        }

        var affectedRows = Convert.ToInt32(affectedRowsParam.Value);

        _logger.LogInformation(
            "Merged {ResourceCount} resources, {AffectedRows} rows affected",
            resources.Count,
            affectedRows);

        // Update extension columns that couldn't be passed through TVPs
        // This runs after MergeResources so the rows exist in the tables
        if (tokenExtensions.Count > 0 || uriExtensions.Count > 0)
        {
            _logger.LogDebug(
                "Updating extension columns: {TokenCount} token, {UriCount} uri",
                tokenExtensions.Count,
                uriExtensions.Count);

            try
            {
                await _extensionUpdater.UpdateAllExtensionsAsync(
                    tokenExtensions,
                    uriExtensions,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to update extension columns after merge (TenantId={TenantId}, ResourceCount={ResourceCount}, TokenExtensionCount={TokenExtensionCount}, UriExtensionCount={UriExtensionCount}). Core resource data was successfully merged; extension columns remain NULL.",
                    tenantId,
                    resources.Count,
                    tokenExtensions.Count,
                    uriExtensions.Count);
            }
        }

        // Persist semantic vectors, strictly after the core merge above has committed (never inside the
        // same transaction -- see MergeVectorSearchParams.sql's header). Only resources whose VectorIndices
        // was actually evaluated participate: null means semantic indexing did not run for this write (the
        // feature is off, or this write path doesn't run it) and any already-persisted vectors must be left
        // alone, so such a resource must not appear in @Evaluated at all. An empty (non-null) list DOES
        // still participate -- the resource's semantic text was evaluated and found empty (e.g. removed on
        // update), and dbo.MergeVectorSearchParams deletes that resource's prior vectors precisely because
        // it is listed as evaluated with nothing in @Vectors (Review Focus 4).
        //
        // singleTransaction is checked again here, not just in the guard above: the guard above only ever
        // throws for a batch that already carries vectors, so this condition is never false by the time
        // control reaches here for such a batch. It stays explicit anyway -- "the writer runs only under
        // singleTransaction" should be visible at the call site itself, not only provable by tracing back
        // to a guard several dozen lines earlier.
        if (_vectorIndexWriter is not null && singleTransaction)
        {
            var vectorEvaluations = BuildVectorEvaluations(resources, resourceTypeIdMap, resourceSurrogateIdMap);
            if (vectorEvaluations.Count > 0)
            {
                try
                {
                    await _vectorIndexWriter.WriteAsync(vectorEvaluations, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Never fail an already-committed resource write because vector persistence failed
                    // (Global Constraints). This intentionally also swallows a caller-cancelled
                    // cancellationToken surfacing here as OperationCanceledException: the core resources
                    // above are committed either way, so a cancellation during this best-effort step is
                    // just another reason vectors may be stale, not a reason to report the whole merge as
                    // failed or cancelled.
                    _logger.LogError(
                        ex,
                        "Failed to persist semantic vectors after merge (TenantId={TenantId}). Affected resources: {AffectedResources}. " +
                        "Core resource data and search indices were successfully merged; semantic vectors may be stale or missing.",
                        tenantId,
                        FormatAffectedResourcesForLog(vectorEvaluations));
                }
            }
        }

        return affectedRows;
    }

    /// <summary>
    /// Builds the writer's evaluated-resource list from this call's own surrogate/type maps, including only
    /// resources whose <see cref="ResourceWrapper.VectorIndices"/> is non-null (see the call site's remarks
    /// on why a null entry must not appear here at all).
    /// </summary>
    private static IReadOnlyList<(short ResourceTypeId, long ResourceSurrogateId, IReadOnlyList<VectorIndexEntry> Entries)> BuildVectorEvaluations(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap)
    {
        List<(short, long, IReadOnlyList<VectorIndexEntry>)>? evaluations = null;
        foreach (var resource in resources)
        {
            if (resource.VectorIndices is null)
            {
                continue;
            }

            // Every resource reaching this point was already accepted by the core merge above, which
            // means ResourceRowGenerator already proved resourceTypeIdMap contains its ResourceType (it
            // throws otherwise) and resourceSurrogateIdMap is built directly from this same resources list
            // -- both lookups are guaranteed to succeed, so a miss here would be a genuine bug, not a
            // recoverable condition to skip past quietly.
            evaluations ??= [];
            evaluations.Add((resourceTypeIdMap[resource.ResourceType], resourceSurrogateIdMap[resource], resource.VectorIndices));
        }

        return (IReadOnlyList<(short, long, IReadOnlyList<VectorIndexEntry>)>?)evaluations ?? [];
    }

    private const int MaxLoggedAffectedResources = 20;

    private static string FormatAffectedResourcesForLog(
        IReadOnlyList<(short ResourceTypeId, long ResourceSurrogateId, IReadOnlyList<VectorIndexEntry> Entries)> evaluations)
    {
        var shown = string.Join(", ", evaluations.Take(MaxLoggedAffectedResources)
            .Select(e => $"{e.ResourceTypeId}/{e.ResourceSurrogateId}"));

        return evaluations.Count > MaxLoggedAffectedResources
            ? $"{shown}, ... ({evaluations.Count - MaxLoggedAffectedResources} more)"
            : shown;
    }

    /// <summary>
    /// Commits a merge transaction.
    /// </summary>
    /// <param name="transactionId">The transaction ID to commit.</param>
    /// <param name="failureReason">Optional failure reason (null indicates success).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CommitTransactionAsync(
        long transactionId,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Committing transaction {TransactionId}, FailureReason={FailureReason}",
            transactionId,
            failureReason ?? "None");

        // Completion does not publish VisibleDate. Advance only the contiguous completed prefix,
        // preserving the legacy visibility barrier for earlier unfinished allocations.
        using var command = new SqlCommand(
            "EXEC dbo.MergeResourcesCommitTransaction @TransactionId, @FailureReason; " +
            "EXEC dbo.MergeResourcesAdvanceTransactionVisibility;")
        {
            CommandType = CommandType.Text
        };
        command.Parameters.Add(new SqlParameter("@TransactionId", SqlDbType.BigInt) { Value = transactionId });
        command.Parameters.Add(new SqlParameter("@FailureReason", SqlDbType.NVarChar)
        {
            Value = failureReason ?? (object)DBNull.Value
        });

        await _sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);

        _logger.LogInformation(
            "Transaction {TransactionId} committed, Success={Success}",
            transactionId,
            failureReason == null);
    }

    /// <summary>
    /// Sends heartbeat for long-running transaction (prevents timeout).
    /// </summary>
    /// <param name="transactionId">The transaction ID to heartbeat.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PutTransactionHeartbeatAsync(
        long transactionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("Sending heartbeat for transaction {TransactionId}", transactionId);

        using var command = new SqlCommand("EXEC dbo.MergeResourcesPutTransactionHeartbeat @TransactionId")
        {
            CommandType = CommandType.Text
        };
        command.Parameters.Add(new SqlParameter("@TransactionId", SqlDbType.BigInt) { Value = transactionId });

        await _sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);
    }

    /// <summary>
    /// Materializes enumerable to list or null if empty.
    /// SqlClient requires NULL (not empty IEnumerable) for TVPs.
    /// This prevents "There are no records in the SqlDataRecord enumeration" error.
    /// </summary>
    private static IList<SqlDataRecord>? MaterializeIfNotEmpty(IEnumerable<SqlDataRecord> records)
    {
        var list = records as IList<SqlDataRecord> ?? records.ToList();
        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// Builds a mapping from ResourceWrapper to ResourceSurrogateId.
    /// Formula: surrogateId = transactionId + entryIndex (bundle entry position)
    /// </summary>
    private static IReadOnlyDictionary<ResourceWrapper, long> BuildResourceSurrogateIdMap(
        long transactionId,
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyList<int> entryIndices)
    {
        var map = new Dictionary<ResourceWrapper, long>(resources.Count);
        for (int i = 0; i < resources.Count; i++)
        {
            map[resources[i]] = transactionId + entryIndices[i];
        }
        return map;
    }
}
