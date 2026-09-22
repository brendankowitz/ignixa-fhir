using System.Data;
using System.Runtime.CompilerServices;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// History-query cluster extracted from <see cref="SqlServerFhirRepository"/>: builds and executes
/// bounded resource/type/system pages and body-free counts. All time filtering uses the same
/// persisted surrogate timestamp as <see cref="SearchEntryResult.LastModified"/>.
/// </summary>
public class SqlServerHistoryQueryExecutor(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    GzipResourceCompressor compressor,
    ILogger logger)
{
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));
    private readonly GzipResourceCompressor _compressor =
        compressor ?? throw new ArgumentNullException(nameof(compressor));
    private readonly ILogger _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly int _tenantId = tenantId;

    public async IAsyncEnumerable<SearchEntryResult> GetResourceHistoryAsync(
        short resourceTypeId,
        string resourceType,
        string resourceId,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string selectFromWhere =
            """
            SELECT r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, r.ResourceSurrogateId, @ResourceTypeName AS ResourceTypeName
            FROM dbo.Resource r
            WHERE r.ResourceTypeId = @ResourceTypeId AND r.ResourceId = @ResourceId
            """;

        await foreach (var result in ExecuteHistoryQueryAsync(
            selectFromWhere,
            command =>
            {
                command.Parameters.Add("@ResourceTypeName", SqlDbType.NVarChar).Value = resourceType;
                command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
                command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;
            },
            parameters,
            cancellationToken))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<SearchEntryResult> GetTypeHistoryAsync(
        short resourceTypeId,
        string resourceType,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string selectFromWhere =
            """
            SELECT r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, r.ResourceSurrogateId, @ResourceTypeName AS ResourceTypeName
            FROM dbo.Resource r
            WHERE r.ResourceTypeId = @ResourceTypeId
            """;

        await foreach (var result in ExecuteHistoryQueryAsync(
            selectFromWhere,
            command =>
            {
                command.Parameters.Add("@ResourceTypeName", SqlDbType.NVarChar).Value = resourceType;
                command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId;
            },
            parameters,
            cancellationToken))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<SearchEntryResult> GetSystemHistoryAsync(
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string selectFromWhere =
            """
            SELECT r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, r.ResourceSurrogateId, rt.Name AS ResourceTypeName
            FROM dbo.Resource r
            JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
            WHERE 1=1
            """;

        await foreach (var result in ExecuteHistoryQueryAsync(selectFromWhere, static _ => { }, parameters, cancellationToken))
        {
            yield return result;
        }
    }

    internal async Task<int> CountHistoryAsync(
        short? resourceTypeId,
        string? resourceId,
        HistoryQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        const string selectFromWhere =
            """
            SELECT COUNT_BIG(*)
            FROM dbo.Resource r
            JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
            WHERE 1=1
            """;
        using var command = CreateHistoryCommand(selectFromWhere, parameters);
        if (resourceTypeId.HasValue)
        {
            command.CommandText += " AND r.ResourceTypeId = @ResourceTypeId";
            command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = resourceTypeId.Value;
        }

        if (resourceId != null)
        {
            command.CommandText += " AND r.ResourceId = @ResourceId";
            command.Parameters.Add("@ResourceId", SqlDbType.VarChar).Value = resourceId;
        }

        var counts = await _sqlExecutionService.ExecuteReaderAsync(
            _tenantId, command, static reader => reader.GetInt64(0), cancellationToken);
        return checked((int)counts.Single());
    }

    // ExecuteReaderAsync materializes a bounded page, never the entire history.
    private async IAsyncEnumerable<SearchEntryResult> ExecuteHistoryQueryAsync(
        string selectFromWhere,
        Action<SqlCommand> configureBaseParameters,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        parameters = parameters.Validate();
        using var command = CreateHistoryCommand(selectFromWhere, parameters);
        var direction = parameters.Sort == HistorySortOrder.Ascending ? "ASC" : "DESC";
        // Only fixed SQL fragments and an enum-derived direction are concatenated.
#pragma warning disable CA2100
        command.CommandText += $" ORDER BY r.ResourceSurrogateId {direction}, r.ResourceTypeId {direction}"
            + " OFFSET @Offset ROWS FETCH NEXT @CountPlusOne ROWS ONLY;";
#pragma warning restore CA2100
        configureBaseParameters(command);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = parameters.Offset;
        command.Parameters.Add("@CountPlusOne", SqlDbType.Int).Value = parameters.Count + 1;

        var rows = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, ReadHistoryRow, cancellationToken);

        for (var i = 0; i < rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The lookahead proves existence without deserializing a body that is not in this page.
            // A corrupt body in the actual page must fail, not silently disappear from history.
            yield return i >= parameters.Count ? PagingProbeSentinel : MapHistoryRow(rows[i]);
        }
    }

    /// <summary>
    /// A reusable, content-free sentinel: <see cref="SearchEntryResult.IsPagingProbe"/> is the only
    /// thing about it a consumer may read, so one immutable instance safely stands in for every
    /// probe-row miss across every history query.
    /// </summary>
    private static readonly SearchEntryResult PagingProbeSentinel = new(
        ResourceType: string.Empty,
        ResourceId: string.Empty,
        VersionId: string.Empty,
        LastModified: DateTimeOffset.UnixEpoch,
        ResourceBytes: ReadOnlyMemory<byte>.Empty)
    {
        IsPagingProbe = true,
    };

    private static SqlCommand CreateHistoryCommand(string selectFromWhere, HistoryQueryParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        // The caller supplies a fixed SQL literal; every data value is a parameter.
#pragma warning disable CA2100
        var command = new SqlCommand(selectFromWhere);
#pragma warning restore CA2100

        if (parameters.Since.HasValue)
        {
            decimal sinceId = decimal.Ceiling((decimal)parameters.Since.Value.UtcTicks / TimeSpan.TicksPerMillisecond)
                * SurrogateIdsPerMillisecond;
            if (sinceId > long.MaxValue)
            {
                command.CommandText += " AND 1=0";
            }
            else
            {
                command.CommandText += " AND r.ResourceSurrogateId >= @SinceId";
                command.Parameters.Add("@SinceId", SqlDbType.BigInt).Value = (long)sinceId;
            }
        }

        if (parameters.Until.HasValue)
        {
            decimal untilExclusiveId = ((decimal)(parameters.Until.Value.UtcTicks / TimeSpan.TicksPerMillisecond) + 1)
                * SurrogateIdsPerMillisecond;
            if (untilExclusiveId <= long.MaxValue)
            {
                command.CommandText += " AND r.ResourceSurrogateId < @UntilExclusiveId";
                command.Parameters.Add("@UntilExclusiveId", SqlDbType.BigInt).Value = (long)untilExclusiveId;
            }
        }

        return command;
    }

    // IdHelper shifts millisecond-truncated ticks left by 3. ToDate discards all 80,000
    // uniquifier values within that millisecond. Inclusive bounds therefore ceil the lower
    // instant and use the next millisecond exclusively for the upper instant. Decimal arithmetic
    // also permits FHIR instants outside the bigint timestamp range without overflow.
    private const long SurrogateIdsPerMillisecond = TimeSpan.TicksPerMillisecond << 3;

    private SearchEntryResult MapHistoryRow(HistoryRow row)
    {
        try
        {
            var resourceBytes = _compressor.DecompressBytes(row.RawResource);
            if (resourceBytes.IsEmpty)
            {
                throw new InvalidDataException("History resource body is empty.");
            }

            var resourceTypeName = row.ResourceTypeName ?? "Unknown";

            return new SearchEntryResult(
                ResourceType: resourceTypeName,
                ResourceId: row.ResourceId,
                VersionId: row.Version.ToString(),
                LastModified: row.ResourceSurrogateId.ToDate(),
                ResourceBytes: resourceBytes)
            {
                IsDeleted = row.IsDeleted,
                Request = new ResourceRequest(row.RequestMethod ?? "PUT", $"{resourceTypeName}/{row.ResourceId}")
            };
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Failed to deserialize history resource {ResourceId} version {Version}", row.ResourceId, row.Version);
            throw;
        }
    }

    private static HistoryRow ReadHistoryRow(SqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetInt32(1),
        (byte[])reader[2],
        reader.GetBoolean(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetInt64(5),
        reader.IsDBNull(6) ? null : reader.GetString(6));

    private readonly record struct HistoryRow(
        string ResourceId,
        int Version,
        byte[] RawResource,
        bool IsDeleted,
        string? RequestMethod,
        long ResourceSurrogateId,
        string? ResourceTypeName);
}
