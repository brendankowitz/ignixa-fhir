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
    private const string BarrierParameterId = "Conformance.MinAcceptedDefinitionsEventId";

    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly int _tenantId = tenantId;
    private readonly GzipResourceCompressor _compressor =
        compressor ?? throw new ArgumentNullException(nameof(compressor));
    private readonly SqlServerSearchIndexReferenceDataCache _referenceDataCache =
        referenceDataCache ?? throw new ArgumentNullException(nameof(referenceDataCache));
    private readonly SqlServerPostMergeExtensionUpdater _extensionUpdater =
        extensionUpdater ?? throw new ArgumentNullException(nameof(extensionUpdater));
    private readonly ILogger _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

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

    public async Task<(long TransactionId, long SurrogateId)> RaiseBarrierAsync(
        long targetEventId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetEventId);

        // IGNORE_DUP_KEY makes a concurrent insert a warning. The second monotonic update ensures a
        // higher target still wins that race before the cutoff is read.
        using var command = new SqlCommand(
            """
            UPDATE dbo.Parameters
            SET Bigint = @TargetEventId
            WHERE Id = @BarrierId
              AND (Bigint IS NULL OR Bigint < @TargetEventId);

            IF NOT EXISTS (SELECT 1 FROM dbo.Parameters WHERE Id = @BarrierId)
            BEGIN
                INSERT INTO dbo.Parameters (Id, Bigint) VALUES (@BarrierId, @TargetEventId);
            END;

            UPDATE dbo.Parameters
            SET Bigint = @TargetEventId
            WHERE Id = @BarrierId
              AND (Bigint IS NULL OR Bigint < @TargetEventId);

            SELECT
                ISNULL((SELECT MAX(SurrogateIdRangeFirstValue) FROM dbo.Transactions), -1),
                ISNULL((
                    SELECT MAX(CutoffValue)
                    FROM (
                        SELECT MAX(SurrogateIdRangeLastValue) AS CutoffValue FROM dbo.Transactions
                        UNION ALL
                        SELECT MAX(ResourceSurrogateId) AS CutoffValue FROM dbo.Resource
                    ) AS CutoffCandidates), -1);
            """);
        command.Parameters.Add("@TargetEventId", SqlDbType.BigInt).Value = targetEventId;
        command.Parameters.Add("@BarrierId", SqlDbType.VarChar, 128).Value = BarrierParameterId;

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            static reader => (TransactionId: reader.GetInt64(0), SurrogateId: reader.GetInt64(1)),
            cancellationToken,
            SqlCommandIdempotency.NonIdempotent);

        return rows.Single();
    }

    public async Task<long> GetVisibleWatermarkAsync(CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            """
            SELECT ISNULL((
                SELECT TOP (1) SurrogateIdRangeFirstValue
                FROM dbo.Transactions
                WHERE IsVisible = 1
                ORDER BY SurrogateIdRangeFirstValue DESC), -1);
            """);
        var values = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId, command, static reader => reader.GetInt64(0), cancellationToken);
        return values.Single();
    }

    public async Task<(long TransactionId, DateTime CreateDate, DateTime HeartbeatDate)?> GetOldestIncompleteTransactionAsync(
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
        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            static reader => (
                TransactionId: reader.GetInt64(0),
                CreateDate: reader.GetDateTime(1),
                HeartbeatDate: reader.GetDateTime(2)),
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<(IReadOnlyList<(long Start, long End, long ResourceCount)> Ranges, long? NextStartAfter)> GetSurrogateIdRangesAsync(
        string resourceType,
        long startAfterSurrogateId,
        long upperBoundSurrogateId,
        int targetRangeSize,
        int maxRanges,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentOutOfRangeException.ThrowIfLessThan(startAfterSurrogateId, -1);
        ArgumentOutOfRangeException.ThrowIfNegative(targetRangeSize);
        ArgumentOutOfRangeException.ThrowIfZero(targetRangeSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRanges);
        ArgumentOutOfRangeException.ThrowIfZero(maxRanges);

        var resourceTypeId = await GetResourceTypeIdAsync(resourceType, cancellationToken);
        if (!resourceTypeId.HasValue || startAfterSurrogateId >= upperBoundSurrogateId)
        {
            return ([], null);
        }

        // Each seek reads at most targetRangeSize rows from the clustered resource key. The resulting
        // ranges deliberately cover ID gaps. Pages continue from the preceding range end, so the
        // caller can discard each page without losing the contiguous cutoff partition.
        var ranges = new List<(long Start, long End, long ResourceCount)>();
        var cursor = startAfterSurrogateId;
        while (true)
        {
            using var nextRangeCommand = new SqlCommand(
                """
                SELECT MIN(ResourceSurrogateId), MAX(ResourceSurrogateId), COUNT_BIG(*)
                FROM (
                    SELECT TOP (@TargetRangeSize) ResourceSurrogateId
                    FROM dbo.Resource
                    WHERE ResourceTypeId = @ResourceTypeId
                      AND IsHistory = 0
                      AND IsDeleted = 0
                      AND ResourceSurrogateId > @Cursor
                      AND ResourceSurrogateId <= @UpperBoundSurrogateId
                    ORDER BY ResourceSurrogateId
                ) AS CurrentRange;
                """);
            nextRangeCommand.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;
            nextRangeCommand.Parameters.Add("@UpperBoundSurrogateId", SqlDbType.BigInt).Value = upperBoundSurrogateId;
            nextRangeCommand.Parameters.Add("@TargetRangeSize", SqlDbType.Int).Value = targetRangeSize;
            nextRangeCommand.Parameters.Add("@Cursor", SqlDbType.BigInt).Value = cursor;
            var rangeEnds = await _sqlExecutionService.ExecuteReaderAsync(
                _tenantId,
                nextRangeCommand,
                static reader => (
                    Start: reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0),
                    End: reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                    Count: reader.GetInt64(2)),
                cancellationToken);
            var (first, end, count) = rangeEnds.Single();
            if (!end.HasValue)
            {
                if (ranges.Count > 0)
                {
                    ranges[^1] = (
                        ranges[^1].Start,
                        upperBoundSurrogateId,
                        ranges[^1].ResourceCount);
                }

                return (ranges, null);
            }

            var rangeStart = cursor == startAfterSurrogateId && startAfterSurrogateId == -1
                ? first!.Value
                : checked(cursor + 1);
            if (count < targetRangeSize || end.Value == upperBoundSurrogateId)
            {
                ranges.Add((rangeStart, upperBoundSurrogateId, count));
                return (ranges, null);
            }

            ranges.Add((rangeStart, end.Value, count));
            if (ranges.Count == maxRanges)
            {
                using var continuationCommand = new SqlCommand(
                    """
                    SELECT CASE WHEN EXISTS (
                        SELECT 1
                        FROM dbo.Resource
                        WHERE ResourceTypeId = @ResourceTypeId
                          AND IsHistory = 0
                          AND IsDeleted = 0
                          AND ResourceSurrogateId > @Cursor
                          AND ResourceSurrogateId <= @UpperBoundSurrogateId)
                        THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
                    """);
                continuationCommand.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;
                continuationCommand.Parameters.Add("@UpperBoundSurrogateId", SqlDbType.BigInt).Value = upperBoundSurrogateId;
                continuationCommand.Parameters.Add("@Cursor", SqlDbType.BigInt).Value = end.Value;
                var hasMore = await _sqlExecutionService.ExecuteReaderAsync(
                    _tenantId,
                    continuationCommand,
                    static reader => reader.GetBoolean(0),
                    cancellationToken);
                if (!hasMore.Single())
                {
                    ranges[^1] = (
                        ranges[^1].Start,
                        upperBoundSurrogateId,
                        ranges[^1].ResourceCount);
                    return (ranges, null);
                }

                return (ranges, end.Value);
            }

            cursor = end.Value;
        }
    }

    public async Task<IReadOnlyList<ReindexResource>> ReadRangeAsync(
        string resourceType,
        long start,
        long endSurrogateId,
        int maxCount,
        long? afterSurrogateId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
        ArgumentOutOfRangeException.ThrowIfZero(maxCount);

        var resourceTypeId = await GetResourceTypeIdAsync(resourceType, cancellationToken);
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
                   r.ResourceSurrogateId,
                   r.RequestMethod,
                   t.CreateDate
            FROM dbo.Resource r
            LEFT JOIN dbo.Transactions t ON t.SurrogateIdRangeFirstValue = r.TransactionId
            WHERE r.ResourceTypeId = @ResourceTypeId
              AND r.IsHistory = 0
              AND r.IsDeleted = 0
              AND r.ResourceSurrogateId >= @Start
              AND r.ResourceSurrogateId <= @End
              AND (@AfterSurrogateId IS NULL OR r.ResourceSurrogateId > @AfterSurrogateId)
            ORDER BY r.ResourceSurrogateId;
            """);
        command.Parameters.Add("@MaxCount", SqlDbType.Int).Value = maxCount;
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;
        command.Parameters.Add("@Start", SqlDbType.BigInt).Value = start;
        command.Parameters.Add("@End", SqlDbType.BigInt).Value = endSurrogateId;
        command.Parameters.Add("@AfterSurrogateId", SqlDbType.BigInt).Value =
            afterSurrogateId ?? (object)DBNull.Value;

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            static reader => new ReindexResourceRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetFieldValue<byte[]>(2),
                reader.GetInt64(3),
                reader.IsDBNull(4) ? "PUT" : reader.GetString(4),
                reader.IsDBNull(5)
                    ? DateTimeOffset.UtcNow
                    : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))),
            cancellationToken);

        return rows.Select(row => new ReindexResource(
            new ResourceWrapper(
                resourceType,
                row.ResourceId,
                row.Version.ToString(CultureInfo.InvariantCulture),
                row.LastModified,
                ResourceJsonNode.Parse(Encoding.UTF8.GetString(_compressor.DecompressBytes(row.RawResource).Span)),
                new ResourceRequest(row.RequestMethod, $"{resourceType}/{row.ResourceId}")),
            row.ResourceSurrogateId)).ToArray();
    }

    public async Task<ReindexCurrentResource> ReadCurrentResourceAsync(
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentException.ThrowIfNullOrEmpty(resourceId);

        var resourceTypeId = await GetResourceTypeIdAsync(resourceType, cancellationToken);
        if (!resourceTypeId.HasValue)
        {
            return new ReindexCurrentResource(null, false);
        }

        using var command = new SqlCommand(
            """
            SELECT TOP (1)
                   r.ResourceId,
                   r.Version,
                   r.RawResource,
                   r.ResourceSurrogateId,
                   r.RequestMethod,
                   t.CreateDate,
                   r.IsDeleted
            FROM dbo.Resource r
            LEFT JOIN dbo.Transactions t ON t.SurrogateIdRangeFirstValue = r.TransactionId
            WHERE r.ResourceTypeId = @ResourceTypeId
              AND r.ResourceId = @ResourceId
              AND r.IsHistory = 0;
            """);
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;
        command.Parameters.Add("@ResourceId", SqlDbType.VarChar, 64).Value = resourceId;
        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            static reader => new ReindexCurrentResourceRow(
                new ReindexResourceRow(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetFieldValue<byte[]>(2),
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? "PUT" : reader.GetString(4),
                    reader.IsDBNull(5)
                        ? DateTimeOffset.UtcNow
                        : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))),
                reader.GetBoolean(6)),
            cancellationToken);
        if (rows.Count == 0)
        {
            return new ReindexCurrentResource(null, false);
        }

        var row = rows.Single();
        if (row.IsDeleted)
        {
            return new ReindexCurrentResource(null, true);
        }

        return new ReindexCurrentResource(
            new ReindexResource(
                new ResourceWrapper(
                    resourceType,
                    row.Resource.ResourceId,
                    row.Resource.Version.ToString(CultureInfo.InvariantCulture),
                    row.Resource.LastModified,
                    ResourceJsonNode.Parse(Encoding.UTF8.GetString(_compressor.DecompressBytes(row.Resource.RawResource).Span)),
                    new ResourceRequest(row.Resource.RequestMethod, $"{resourceType}/{row.Resource.ResourceId}")),
                row.Resource.ResourceSurrogateId),
            false);
    }

    public async Task<(int Updated, int Conflicts)> UpdateSearchIndicesAsync(
        IReadOnlyList<ReindexResource> resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Count == 0)
        {
            return (0, 0);
        }

        await _referenceDataCache.EnsureResourceTypesPreloadedAsync(cancellationToken);
        await _referenceDataCache.EnsureSearchParametersPreloadedAsync(cancellationToken);

        var resourceWrappers = resources.Select(resource => resource.Resource).ToArray();
        var resourceTypeIdMap = _referenceDataCache.ResourceTypeMappings;
        var searchParameterIdMap = _referenceDataCache.SearchParameterMappings;
        var resourceSurrogateIdMap = resources.ToDictionary(resource => resource.Resource, resource => resource.ResourceSurrogateId);

        var resourceRecords = CreateResourceRecords(resources, resourceTypeIdMap);
        var resourceWriteClaims = await ReadResourceWriteClaimsAsync(resources, cancellationToken);
        var referenceSearchParams = MaterializeIfNotEmpty(
            _referenceRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenSearchParams = MaterializeIfNotEmpty(
            _tokenRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenTexts = MaterializeIfNotEmpty(
            _tokenTextRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var stringSearchParams = MaterializeIfNotEmpty(
            _stringRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var uriSearchParams = MaterializeIfNotEmpty(
            _uriRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var numberSearchParams = MaterializeIfNotEmpty(
            _numberRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var quantitySearchParams = MaterializeIfNotEmpty(
            _quantityRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var dateTimeSearchParams = MaterializeIfNotEmpty(
            _dateTimeRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var referenceTokenComposites = MaterializeIfNotEmpty(
            _refTokenCompositeRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenTokenComposites = MaterializeIfNotEmpty(
            _tokenTokenCompositeRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenDateTimeComposites = MaterializeIfNotEmpty(
            _tokenDateTimeCompositeRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenQuantityComposites = MaterializeIfNotEmpty(
            _tokenQuantityCompositeRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenStringComposites = MaterializeIfNotEmpty(
            _tokenStringCompositeRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
        var tokenNumberNumberComposites = MaterializeIfNotEmpty(
            _tokenNumberNumberCompositeRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));

        var failedResources = new SqlParameter("@FailedResources", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        using var command = new SqlCommand(
            """
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
            """);
        command.Parameters.Add(failedResources);
        AddTableValuedParameter(command, "@Resources", "dbo.ResourceList", resourceRecords);
        AddTableValuedParameter(command, "@ResourceWriteClaims", "dbo.ResourceWriteClaimList", resourceWriteClaims);
        AddTableValuedParameter(command, "@ReferenceSearchParams", "dbo.ReferenceSearchParamList", referenceSearchParams);
        AddTableValuedParameter(command, "@TokenSearchParams", "dbo.TokenSearchParamList", tokenSearchParams);
        AddTableValuedParameter(command, "@TokenTexts", "dbo.TokenTextList", tokenTexts);
        AddTableValuedParameter(command, "@StringSearchParams", "dbo.StringSearchParamList", stringSearchParams);
        AddTableValuedParameter(command, "@UriSearchParams", "dbo.UriSearchParamList", uriSearchParams);
        AddTableValuedParameter(command, "@NumberSearchParams", "dbo.NumberSearchParamList", numberSearchParams);
        AddTableValuedParameter(command, "@QuantitySearchParams", "dbo.QuantitySearchParamList", quantitySearchParams);
        AddTableValuedParameter(command, "@DateTimeSearchParams", "dbo.DateTimeSearchParamList", dateTimeSearchParams);
        AddTableValuedParameter(command, "@ReferenceTokenCompositeSearchParams", "dbo.ReferenceTokenCompositeSearchParamList", referenceTokenComposites);
        AddTableValuedParameter(command, "@TokenTokenCompositeSearchParams", "dbo.TokenTokenCompositeSearchParamList", tokenTokenComposites);
        AddTableValuedParameter(command, "@TokenDateTimeCompositeSearchParams", "dbo.TokenDateTimeCompositeSearchParamList", tokenDateTimeComposites);
        AddTableValuedParameter(command, "@TokenQuantityCompositeSearchParams", "dbo.TokenQuantityCompositeSearchParamList", tokenQuantityComposites);
        AddTableValuedParameter(command, "@TokenStringCompositeSearchParams", "dbo.TokenStringCompositeSearchParamList", tokenStringComposites);
        AddTableValuedParameter(command, "@TokenNumberNumberCompositeSearchParams", "dbo.TokenNumberNumberCompositeSearchParamList", tokenNumberNumberComposites);

        try
        {
            await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            throw new TimeoutException("The SQL reindex write timed out.", ex);
        }

        var conflicts = Convert.ToInt32(failedResources.Value, CultureInfo.InvariantCulture);
        var tokenExtensions = _tokenRowGenerator.ExtractExtensionData(
            resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToArray();
        var uriExtensions = _uriRowGenerator.ExtractExtensionData(
            resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToArray();
        if (tokenExtensions.Length > 0 || uriExtensions.Length > 0)
        {
            try
            {
                await _extensionUpdater.UpdateAllExtensionsAsync(tokenExtensions, uriExtensions, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to update extension columns after reindex (TenantId={TenantId}, ResourceCount={ResourceCount}, TokenExtensionCount={TokenExtensionCount}, UriExtensionCount={UriExtensionCount}). Core search indices were successfully updated; extension columns remain NULL.",
                    _tenantId,
                    resources.Count,
                    tokenExtensions.Length,
                    uriExtensions.Length);
            }
        }

        return (resources.Count - conflicts, conflicts);
    }

    public async Task<bool> HasSearchParameterAsync(
        int searchParamId,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            "SELECT SearchParamId FROM dbo.SearchParam WHERE SearchParamId = @SearchParamId");
        command.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value =
            checked((short)searchParamId);
        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            static reader => reader.GetInt16(0),
            cancellationToken);
        return rows.Count != 0;
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
        var claims = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
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

    private async Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
    {
        await _referenceDataCache.EnsureResourceTypesPreloadedAsync(cancellationToken);
        return _referenceDataCache.ResourceTypeMappings.TryGetValue(resourceType, out var resourceTypeId)
            ? resourceTypeId
            : null;
    }

    private static void AddTableValuedParameter(
        SqlCommand command,
        string name,
        string typeName,
        IList<SqlDataRecord>? records) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Structured)
        {
            TypeName = typeName,
            Value = records,
        });

    private static IList<SqlDataRecord>? MaterializeIfNotEmpty(IEnumerable<SqlDataRecord> records)
    {
        var list = records as IList<SqlDataRecord> ?? records.ToList();
        return list.Count == 0 ? null : list;
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
