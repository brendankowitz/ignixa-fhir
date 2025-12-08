// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Data;
using System.Collections;
using System.Diagnostics;
using Ignixa.DataLayer.SqlEntityFramework.Compression;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// High-performance repository for bulk resource merging using stored procedures and TVPs.
/// Provides 10-100x performance improvement over EF Core for batch operations.
/// Uses tenant-specific SearchIndexReferenceDataCache for all reference data lookups.
/// </summary>
public class SqlMergeRepository
{
    private readonly FhirDbContext _context;
    private readonly GzipResourceCompressor _compressor;
    private readonly ILogger<SqlMergeRepository> _logger;
    private readonly SearchIndexReferenceDataCache _referenceDataCache;
    private readonly ResourceRowGenerator _resourceRowGenerator;
    private readonly ResourceWriteClaimRowGenerator _resourceWriteClaimRowGenerator;
    private readonly ISearchParameterRowGenerator _tokenRowGenerator;
    private readonly ISearchParameterRowGenerator _referenceRowGenerator;
    private readonly ISearchParameterRowGenerator _stringRowGenerator;
    private readonly ISearchParameterRowGenerator _numberRowGenerator;
    private readonly ISearchParameterRowGenerator _quantityRowGenerator;
    private readonly ISearchParameterRowGenerator _dateTimeRowGenerator;
    private readonly ISearchParameterRowGenerator _uriRowGenerator;
    private readonly ISearchParameterRowGenerator _tokenTextRowGenerator;
    private readonly ISearchParameterRowGenerator _refTokenCompositeRowGenerator;
    private readonly ISearchParameterRowGenerator _tokenTokenCompositeRowGenerator;
    private readonly ISearchParameterRowGenerator _tokenDateTimeCompositeRowGenerator;
    private readonly ISearchParameterRowGenerator _tokenQuantityCompositeRowGenerator;
    private readonly ISearchParameterRowGenerator _tokenStringCompositeRowGenerator;
    private readonly ISearchParameterRowGenerator _tokenNumberNumberCompositeRowGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlMergeRepository"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="compressor">The Gzip resource compressor.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="referenceDataCache">The search index reference data cache.</param>
    public SqlMergeRepository(
        FhirDbContext context,
        GzipResourceCompressor compressor,
        ILogger<SqlMergeRepository> logger,
        SearchIndexReferenceDataCache referenceDataCache)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _referenceDataCache = referenceDataCache ?? throw new ArgumentNullException(nameof(referenceDataCache));

        // Initialize row generators (will be injected in Phase 3 via DI container)
        _resourceRowGenerator = new ResourceRowGenerator(compressor);
        _resourceWriteClaimRowGenerator = new ResourceWriteClaimRowGenerator();
        _tokenRowGenerator = new TokenSearchParameterRowGenerator();
        _referenceRowGenerator = new ReferenceSearchParameterRowGenerator();
        _stringRowGenerator = new StringSearchParameterRowGenerator();
        _numberRowGenerator = new NumberSearchParameterRowGenerator();
        _quantityRowGenerator = new QuantitySearchParameterRowGenerator();
        _dateTimeRowGenerator = new DateTimeSearchParameterRowGenerator();
        _uriRowGenerator = new UriSearchParameterRowGenerator();
        _tokenTextRowGenerator = new TokenTextRowGenerator();
        _refTokenCompositeRowGenerator = new RefTokenCompositeRowGenerator();
        _tokenTokenCompositeRowGenerator = new TokenTokenCompositeRowGenerator();
        _tokenDateTimeCompositeRowGenerator = new TokenDateTimeCompositeRowGenerator();
        _tokenQuantityCompositeRowGenerator = new TokenQuantityCompositeRowGenerator();
        _tokenStringCompositeRowGenerator = new TokenStringCompositeRowGenerator();
        _tokenNumberNumberCompositeRowGenerator = new TokenNumberNumberCompositeRowGenerator();
    }

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

        var countParam = new SqlParameter("@Count", SqlDbType.Int) { Value = resourceCount };
        var heartbeatParam = new SqlParameter("@HeartbeatDate", SqlDbType.DateTime) { Value = DBNull.Value };

        await _context.Database.ExecuteSqlRawAsync(
            "EXEC dbo.MergeResourcesBeginTransaction @Count, @TransactionId OUTPUT, @SequenceRangeFirstValue OUTPUT, @HeartbeatDate",
            new[] { countParam, transactionIdParam, sequenceStartParam, heartbeatParam },
            cancellationToken);

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

        // Ensure cache is preloaded for small reference data (if not already done)
        if (_referenceDataCache.ResourceTypeMappings.Count == 0)
        {
            _logger.LogInformation("Preloading resource type mappings");
            await _referenceDataCache.PreloadResourceTypesAsync();
        }

        if (_referenceDataCache.SearchParameterMappings.Count == 0)
        {
            _logger.LogInformation("Preloading search parameter mappings (limited to 10,000 rows)");
            await _referenceDataCache.PreloadSearchParamsAsync(maxRows: 10000);
        }

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
        var referenceSearchParams = MaterializeIfNotEmpty(_referenceRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        _logger.LogInformation("ReferenceSearchParams count: {Count}", referenceSearchParams?.Count ?? 0);
        var tokenSearchParams = MaterializeIfNotEmpty(_tokenRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        _logger.LogInformation("TokenSearchParams count: {Count}", tokenSearchParams?.Count ?? 0);
        var tokenTexts = MaterializeIfNotEmpty(_tokenTextRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var stringSearchParams = MaterializeIfNotEmpty(_stringRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var uriSearchParams = MaterializeIfNotEmpty(_uriRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var numberSearchParams = MaterializeIfNotEmpty(_numberRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var quantitySearchParams = MaterializeIfNotEmpty(_quantityRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var dateTimeSearchParams = MaterializeIfNotEmpty(_dateTimeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));

        // Generate composite search param SqlDataRecord streams (Phase 2: Using row generators with direct streaming)
        // Materialize and check for empty - SQL Client requires NULL (not empty) for TVPs
        var refTokenCompositeParams = MaterializeIfNotEmpty(_refTokenCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var tokenTokenCompositeParams = MaterializeIfNotEmpty(_tokenTokenCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var tokenDateTimeCompositeParams = MaterializeIfNotEmpty(_tokenDateTimeCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var tokenQuantityCompositeParams = MaterializeIfNotEmpty(_tokenQuantityCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var tokenStringCompositeParams = MaterializeIfNotEmpty(_tokenStringCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
        var tokenNumberNumberCompositeParams = MaterializeIfNotEmpty(_tokenNumberNumberCompositeRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));

        // Create stored procedure parameters
        // NOTE: Using SqlDataRecord streaming (proper TVP pattern) instead of DataTable
        // SqlDataRecord provides better performance and guaranteed column ordering
        // IMPORTANT: Parameter order must match stored procedure signature exactly
        
        var affectedRowsParam = new SqlParameter("@AffectedRows", SqlDbType.Int) { Direction = ParameterDirection.Output };
        
        var parameters = new[]
        {
            affectedRowsParam,
            new SqlParameter("@RaiseExceptionOnConflict", SqlDbType.Bit) { Value = true },
            new SqlParameter("@IsResourceChangeCaptureEnabled", SqlDbType.Bit) { Value = false },
            new SqlParameter("@TransactionId", SqlDbType.BigInt) { Value = transactionId },
            //new SqlParameter("@TransactionId", SqlDbType.BigInt) { Value = DBNull.Value },
            new SqlParameter("@SingleTransaction", SqlDbType.Bit) { Value = singleTransaction },
            new SqlParameter("@Resources", SqlDbType.Structured)
            {
                TypeName = "dbo.ResourceList",
                Value = resourceRecords
            },
            new SqlParameter("@ResourceWriteClaims", SqlDbType.Structured)
            {
                TypeName = "dbo.ResourceWriteClaimList",
                Value = resourceWriteClaimRecords
            },
            new SqlParameter("@ReferenceSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.ReferenceSearchParamList",
                Value = referenceSearchParams
            },
            new SqlParameter("@TokenSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.TokenSearchParamList",
                Value = tokenSearchParams
            },
            new SqlParameter("@TokenTexts", SqlDbType.Structured)
            {
                TypeName = "dbo.TokenTextList",
                Value = tokenTexts
            },
            new SqlParameter("@StringSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.StringSearchParamList",
                Value = stringSearchParams
            },
            new SqlParameter("@UriSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.UriSearchParamList",
                Value = uriSearchParams
            },
            new SqlParameter("@NumberSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.NumberSearchParamList",
                Value = numberSearchParams
            },
            new SqlParameter("@QuantitySearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.QuantitySearchParamList",
                Value = quantitySearchParams
            },
            new SqlParameter("@DateTimeSearchParms", SqlDbType.Structured)
            {
                TypeName = "dbo.DateTimeSearchParamList",
                Value = dateTimeSearchParams
            },
            new SqlParameter("@ReferenceTokenCompositeSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.ReferenceTokenCompositeSearchParamList",
                Value = refTokenCompositeParams
            },
            new SqlParameter("@TokenTokenCompositeSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.TokenTokenCompositeSearchParamList",
                Value = tokenTokenCompositeParams
            },
            new SqlParameter("@TokenDateTimeCompositeSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.TokenDateTimeCompositeSearchParamList",
                Value = tokenDateTimeCompositeParams
            },
            new SqlParameter("@TokenQuantityCompositeSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.TokenQuantityCompositeSearchParamList",
                Value = tokenQuantityCompositeParams
            },
            new SqlParameter("@TokenStringCompositeSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.TokenStringCompositeSearchParamList",
                Value = tokenStringCompositeParams
            },
            new SqlParameter("@TokenNumberNumberCompositeSearchParams", SqlDbType.Structured)
            {
                TypeName = "dbo.TokenNumberNumberCompositeSearchParamList",
                Value = tokenNumberNumberCompositeParams
            }
        };

        try
        {
            // Execute merge stored procedure
            await _context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.MergeResources @AffectedRows OUTPUT, @RaiseExceptionOnConflict, @IsResourceChangeCaptureEnabled, " +
                "@TransactionId, @SingleTransaction, @Resources, @ResourceWriteClaims, " +
                "@ReferenceSearchParams, @TokenSearchParams, @TokenTexts, @StringSearchParams, @UriSearchParams, " +
                "@NumberSearchParams, @QuantitySearchParams, @DateTimeSearchParms, " +
                "@ReferenceTokenCompositeSearchParams, @TokenTokenCompositeSearchParams, " +
                "@TokenDateTimeCompositeSearchParams, @TokenQuantityCompositeSearchParams, @TokenStringCompositeSearchParams, " +
                "@TokenNumberNumberCompositeSearchParams",
                parameters,
                cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 50409)
        {
            // SQL error 50409: Resource has been recently updated or added (version conflict)
            throw new PreconditionFailedException("Resource was recently updated. Please refresh and retry.");
        }

        var affectedRows = Convert.ToInt32(affectedRowsParam.Value);
        Debug.Assert(affectedRows > 0);

        _logger.LogInformation(
            "Merged {ResourceCount} resources, {AffectedRows} rows affected",
            resources.Count,
            affectedRows);

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

        var parameters = new[]
        {
            new SqlParameter("@TransactionId", SqlDbType.BigInt) { Value = transactionId },
            new SqlParameter("@FailureReason", SqlDbType.NVarChar)
            {
                Value = failureReason ?? (object)DBNull.Value
            }
        };

        await _context.Database.ExecuteSqlRawAsync(
            "EXEC dbo.MergeResourcesCommitTransaction @TransactionId, @FailureReason",
            parameters,
            cancellationToken);

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

        var parameter = new SqlParameter("@TransactionId", SqlDbType.BigInt) { Value = transactionId };

        await _context.Database.ExecuteSqlRawAsync(
            "EXEC dbo.MergeResourcesPutTransactionHeartbeat @TransactionId",
            new[] { parameter },
            cancellationToken);
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


    #region TVP Builders (Phase 1: Structure only, full implementation in Phase 2-3)

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

    // NOTE: All TVP generation now uses row generators that stream SqlDataRecord directly
    // All 16 generators (Resource + ResourceWriteClaim + 8 simple search params + 6 composite search params)
    // use GenerateSqlDataRecords() for efficient TVP streaming without intermediate DataTable allocation

    #endregion
}
