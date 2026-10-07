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

    public async Task<(long TransactionId, long SurrogateId)> RaiseBarrierAsync(
        long targetEventId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetEventId);

        // The update/insert each auto-commit before the MAX query. A duplicate-key race on the first
        // insert is retried as the monotonic update rather than being hidden; after either branch the
        // following read necessarily observes the raised barrier.
        using var command = new SqlCommand(
            """
            UPDATE dbo.Parameters
            SET Bigint = @TargetEventId
            WHERE Id = @BarrierId
              AND (Bigint IS NULL OR Bigint < @TargetEventId);

            IF @@ROWCOUNT = 0 AND NOT EXISTS (SELECT 1 FROM dbo.Parameters WHERE Id = @BarrierId)
            BEGIN TRY
                INSERT INTO dbo.Parameters (Id, Bigint) VALUES (@BarrierId, @TargetEventId);
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() IN (2601, 2627)
                    UPDATE dbo.Parameters
                    SET Bigint = @TargetEventId
                    WHERE Id = @BarrierId
                      AND (Bigint IS NULL OR Bigint < @TargetEventId);
                ELSE
                    THROW;
            END CATCH;

            SELECT
                ISNULL(MAX(SurrogateIdRangeFirstValue), -1),
                ISNULL(MAX(SurrogateIdRangeLastValue), -1)
            FROM dbo.Transactions;
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

    public async Task<IReadOnlyList<(long Start, long End)>> GetSurrogateIdRangesAsync(
        string resourceType,
        long upperBoundSurrogateId,
        int targetRangeSize,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentOutOfRangeException.ThrowIfNegative(targetRangeSize);
        ArgumentOutOfRangeException.ThrowIfZero(targetRangeSize);

        var resourceTypeId = await GetResourceTypeIdAsync(resourceType, cancellationToken);
        if (!resourceTypeId.HasValue)
        {
            return [];
        }

        using var command = new SqlCommand(
            """
            WITH CurrentRows AS (
                SELECT ResourceSurrogateId,
                       (ROW_NUMBER() OVER (ORDER BY ResourceSurrogateId) - 1) / @TargetRangeSize AS RangeNumber
                FROM dbo.Resource
                WHERE ResourceTypeId = @ResourceTypeId
                  AND IsHistory = 0
                  AND IsDeleted = 0
                  AND ResourceSurrogateId <= @UpperBoundSurrogateId
            )
            SELECT MIN(ResourceSurrogateId), MAX(ResourceSurrogateId)
            FROM CurrentRows
            GROUP BY RangeNumber
            ORDER BY RangeNumber;
            """);
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;
        command.Parameters.Add("@UpperBoundSurrogateId", SqlDbType.BigInt).Value = upperBoundSurrogateId;
        command.Parameters.Add("@TargetRangeSize", SqlDbType.Int).Value = targetRangeSize;

        return await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId,
            command,
            static reader => (Start: reader.GetInt64(0), End: reader.GetInt64(1)),
            cancellationToken);
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
        var resourceWriteClaims = MaterializeIfNotEmpty(
            _resourceWriteClaimRowGenerator.GenerateSqlDataRecords(resourceWrappers, resourceSurrogateIdMap));
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

        await _sqlExecutionService.ExecuteNonQueryAsync(_tenantId, command, cancellationToken);

        var conflicts = Convert.ToInt32(failedResources.Value, CultureInfo.InvariantCulture);
        var tokenExtensions = _tokenRowGenerator.ExtractExtensionData(
            resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToArray();
        var uriExtensions = _uriRowGenerator.ExtractExtensionData(
            resourceWrappers, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToArray();
        await _extensionUpdater.UpdateAllExtensionsAsync(tokenExtensions, uriExtensions, cancellationToken);

        return (resources.Count - conflicts, conflicts);
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
