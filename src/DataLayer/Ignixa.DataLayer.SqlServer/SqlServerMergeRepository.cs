using System.Data;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.RowGenerators;
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
    ILogger<SqlServerMergeRepository> logger)
{
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly SqlServerSearchIndexReferenceDataCache _referenceDataCache =
        referenceDataCache ?? throw new ArgumentNullException(nameof(referenceDataCache));
    private readonly SqlServerPostMergeExtensionUpdater _extensionUpdater =
        extensionUpdater ?? throw new ArgumentNullException(nameof(extensionUpdater));
    private readonly ILogger<SqlServerMergeRepository> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly ResourceRowGenerator _resourceRowGenerator =
        new(compressor ?? throw new ArgumentNullException(nameof(compressor)));
    private readonly ResourceWriteClaimRowGenerator _resourceWriteClaimRowGenerator = new();
    private readonly TokenSearchParameterRowGenerator _tokenRowGenerator = new(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _referenceRowGenerator = new ReferenceSearchParameterRowGenerator();
    private readonly ISearchParameterRowGenerator _stringRowGenerator = new StringSearchParameterRowGenerator();
    private readonly ISearchParameterRowGenerator _numberRowGenerator = new NumberSearchParameterRowGenerator();
    private readonly ISearchParameterRowGenerator _quantityRowGenerator =
        new QuantitySearchParameterRowGenerator(referenceDataCache.SystemMappings, referenceDataCache.QuantityCodeMappings);
    private readonly ISearchParameterRowGenerator _dateTimeRowGenerator = new DateTimeSearchParameterRowGenerator();
    private readonly UriSearchParameterRowGenerator _uriRowGenerator = new();
    private readonly ISearchParameterRowGenerator _tokenTextRowGenerator = new TokenTextRowGenerator();
    private readonly ISearchParameterRowGenerator _refTokenCompositeRowGenerator =
        new RefTokenCompositeRowGenerator(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _tokenTokenCompositeRowGenerator =
        new TokenTokenCompositeRowGenerator(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _tokenDateTimeCompositeRowGenerator =
        new TokenDateTimeCompositeRowGenerator(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _tokenQuantityCompositeRowGenerator =
        new TokenQuantityCompositeRowGenerator(referenceDataCache.SystemMappings, referenceDataCache.QuantityCodeMappings);
    private readonly ISearchParameterRowGenerator _tokenStringCompositeRowGenerator =
        new TokenStringCompositeRowGenerator(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _tokenNumberNumberCompositeRowGenerator =
        new TokenNumberNumberCompositeRowGenerator(referenceDataCache.SystemMappings);

    /// <summary>
    /// Begins a merge transaction, allocating transaction ID and sequence range.
    /// </summary>
    /// <param name="resourceCount">Number of resources to be merged in this transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing (TransactionId, SequenceRangeFirstValue).</returns>
    public async Task<(long TransactionId, int SequenceStart)> BeginTransactionAsync(
        int resourceCount,
        CancellationToken cancellationToken = default)
    {
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
            "EXEC dbo.MergeResourcesBeginTransaction @Count, @TransactionId OUTPUT, @SequenceRangeFirstValue OUTPUT, @HeartbeatDate")
        {
            CommandType = CommandType.Text
        };
        command.Parameters.Add(new SqlParameter("@Count", SqlDbType.Int) { Value = resourceCount });
        command.Parameters.Add(transactionIdParam);
        command.Parameters.Add(sequenceStartParam);
        command.Parameters.Add(new SqlParameter("@HeartbeatDate", SqlDbType.DateTime) { Value = DBNull.Value });

        await _sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);

        var transactionId = (long)transactionIdParam.Value!;
        var sequenceStart = (int)sequenceStartParam.Value!;

        _logger.LogInformation(
            "Merge transaction started: TransactionId={TransactionId}, SequenceStart={SequenceStart}",
            transactionId,
            sequenceStart);

        return (transactionId, sequenceStart);
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

        // Generate SqlDataRecord streams directly (eliminates DataTable intermediate step)
        // Materialize and check for empty - SQL Client requires NULL (not empty) for TVPs
        var referenceSearchParams = MaterializeIfNotEmpty(_referenceRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        _logger.LogInformation("ReferenceSearchParams count: {Count}", referenceSearchParams?.Count ?? 0);
        var tokenSearchParams = MaterializeIfNotEmpty(_tokenRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        _logger.LogInformation("TokenSearchParams count: {Count}", tokenSearchParams?.Count ?? 0);
        var tokenTexts = MaterializeIfNotEmpty(_tokenTextRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var stringSearchParams = MaterializeIfNotEmpty(_stringRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var uriSearchParams = MaterializeIfNotEmpty(_uriRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var numberSearchParams = MaterializeIfNotEmpty(_numberRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var quantitySearchParams = MaterializeIfNotEmpty(_quantityRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var dateTimeSearchParams = MaterializeIfNotEmpty(_dateTimeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));

        // Generate composite search param SqlDataRecord streams
        // Materialize and check for empty - SQL Client requires NULL (not empty) for TVPs
        var refTokenCompositeParams = MaterializeIfNotEmpty(_refTokenCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenTokenCompositeParams = MaterializeIfNotEmpty(_tokenTokenCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenDateTimeCompositeParams = MaterializeIfNotEmpty(_tokenDateTimeCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenQuantityCompositeParams = MaterializeIfNotEmpty(_tokenQuantityCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenStringCompositeParams = MaterializeIfNotEmpty(_tokenStringCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenNumberNumberCompositeParams = MaterializeIfNotEmpty(_tokenNumberNumberCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));

        // Create stored procedure parameters
        // NOTE: Using SqlDataRecord streaming (proper TVP pattern) instead of DataTable
        // SqlDataRecord provides better performance and guaranteed column ordering
        // IMPORTANT: Parameter order must match stored procedure signature exactly

        var affectedRowsParam = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };

        using var command = new SqlCommand(
            "EXEC dbo.MergeResources @AffectedRows OUTPUT, @RaiseExceptionOnConflict, @IsResourceChangeCaptureEnabled, " +
            "@TransactionId, @SingleTransaction, @Resources, @ResourceWriteClaims, " +
            "@ReferenceSearchParams, @TokenSearchParams, @TokenTexts, @StringSearchParams, @UriSearchParams, " +
            "@NumberSearchParams, @QuantitySearchParams, @DateTimeSearchParms, " +
            "@ReferenceTokenCompositeSearchParams, @TokenTokenCompositeSearchParams, " +
            "@TokenDateTimeCompositeSearchParams, @TokenQuantityCompositeSearchParams, @TokenStringCompositeSearchParams, " +
            "@TokenNumberNumberCompositeSearchParams")
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
        command.Parameters.Add(new SqlParameter("@ReferenceSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.ReferenceSearchParamList",
            Value = referenceSearchParams
        });
        command.Parameters.Add(new SqlParameter("@TokenSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenSearchParamList",
            Value = tokenSearchParams
        });
        command.Parameters.Add(new SqlParameter("@TokenTexts", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenTextList",
            Value = tokenTexts
        });
        command.Parameters.Add(new SqlParameter("@StringSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.StringSearchParamList",
            Value = stringSearchParams
        });
        command.Parameters.Add(new SqlParameter("@UriSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.UriSearchParamList",
            Value = uriSearchParams
        });
        command.Parameters.Add(new SqlParameter("@NumberSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.NumberSearchParamList",
            Value = numberSearchParams
        });
        command.Parameters.Add(new SqlParameter("@QuantitySearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.QuantitySearchParamList",
            Value = quantitySearchParams
        });
        command.Parameters.Add(new SqlParameter("@DateTimeSearchParms", SqlDbType.Structured)
        {
            TypeName = "dbo.DateTimeSearchParamList",
            Value = dateTimeSearchParams
        });
        command.Parameters.Add(new SqlParameter("@ReferenceTokenCompositeSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.ReferenceTokenCompositeSearchParamList",
            Value = refTokenCompositeParams
        });
        command.Parameters.Add(new SqlParameter("@TokenTokenCompositeSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenTokenCompositeSearchParamList",
            Value = tokenTokenCompositeParams
        });
        command.Parameters.Add(new SqlParameter("@TokenDateTimeCompositeSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenDateTimeCompositeSearchParamList",
            Value = tokenDateTimeCompositeParams
        });
        command.Parameters.Add(new SqlParameter("@TokenQuantityCompositeSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenQuantityCompositeSearchParamList",
            Value = tokenQuantityCompositeParams
        });
        command.Parameters.Add(new SqlParameter("@TokenStringCompositeSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenStringCompositeSearchParamList",
            Value = tokenStringCompositeParams
        });
        command.Parameters.Add(new SqlParameter("@TokenNumberNumberCompositeSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenNumberNumberCompositeSearchParamList",
            Value = tokenNumberNumberCompositeParams
        });

        // Extract extension data before calling the SP (needed for post-merge update)
        var tokenExtensions = _tokenRowGenerator.ExtractExtensionData(
            resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToList();
        var uriExtensions = _uriRowGenerator.ExtractExtensionData(
            resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToList();

        try
        {
            // Execute merge stored procedure
            await _sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 50409)
        {
            // SQL error 50409: Resource has been recently updated or added (version conflict)
            throw new PreconditionFailedException("Resource was recently updated. Please refresh and retry.");
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

            await _extensionUpdater.UpdateAllExtensionsAsync(
                tokenExtensions,
                uriExtensions,
                cancellationToken);
        }

        return affectedRows;
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

        using var command = new SqlCommand("EXEC dbo.MergeResourcesCommitTransaction @TransactionId, @FailureReason")
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
