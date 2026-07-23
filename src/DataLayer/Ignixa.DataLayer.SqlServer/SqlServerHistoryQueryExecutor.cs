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
/// the resource/type/system history queries, mapping rows to <see cref="SearchEntryResult"/>. Mirrors
/// SqlEntityFrameworkRepository.ExecuteHistoryQueryAsync (:849-931) -- see that method's original
/// comment for the shared Since/Until/sort/pagination clause and per-row try/catch-and-skip rationale.
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
            FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
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
            FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
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
            LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
            JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
            WHERE 1=1
            """;

        await foreach (var result in ExecuteHistoryQueryAsync(selectFromWhere, static _ => { }, parameters, cancellationToken))
        {
            yield return result;
        }
    }

    // Shared by all 3 history methods above (mirrors SqlEntityFrameworkRepository.ExecuteHistoryQueryAsync,
    // :849-931): appends the Since/Until/sort/pagination clauses common to every history query onto
    // whichever base SELECT/FROM/WHERE the caller supplies, executes it, and maps each row with the
    // same per-row try/catch-and-skip the original uses -- a genuinely malformed RawResource on one
    // history row must not fail the whole page. ISqlExecutionService has no server-side-cursor
    // streaming primitive (ExecuteReaderAsync always fully materializes), so this yields from an
    // already-fetched in-memory page rather than a live DB cursor; the IAsyncEnumerable<T> contract
    // callers see is otherwise identical.
    private async IAsyncEnumerable<SearchEntryResult> ExecuteHistoryQueryAsync(
        string selectFromWhere,
        Action<SqlCommand> configureBaseParameters,
        HistoryQueryParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // CA2100 suppressed: sql is built from a fixed caller-supplied literal plus at most two fixed
        // literal filter fragments and a sort direction drawn from a 2-value enum (never free-form
        // caller input) -- same rationale as DeleteSearchIndexEntriesAsync's suppression above. Every
        // actual value (ResourceTypeId, ResourceId, Since, Until, Offset, CountPlusOne) flows through
        // parameters, never string concatenation.
#pragma warning disable CA2100
        using var command = new SqlCommand(BuildHistorySql(selectFromWhere, parameters));
#pragma warning restore CA2100
        configureBaseParameters(command);
        AddSharedHistoryParameters(command, parameters);

        var rows = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, ReadHistoryRow, cancellationToken);

        foreach (var row in rows)
        {
            var result = TryMapHistoryRow(row);
            if (result != null)
            {
                yield return result;
            }
        }
    }

    private static string BuildHistorySql(string selectFromWhere, HistoryQueryParameters parameters)
    {
        var direction = parameters.Sort == HistorySortOrder.Ascending ? "ASC" : "DESC";
        var sql = selectFromWhere;

        if (parameters.Since.HasValue)
        {
            sql += " AND t.CreateDate >= @Since";
        }

        if (parameters.Until.HasValue)
        {
            sql += " AND t.CreateDate <= @Until";
        }

        return sql
            + $" ORDER BY t.CreateDate {direction}, r.ResourceSurrogateId {direction}"
            + " OFFSET @Offset ROWS FETCH NEXT @CountPlusOne ROWS ONLY;";
    }

    private static void AddSharedHistoryParameters(SqlCommand command, HistoryQueryParameters parameters)
    {
        if (parameters.Since.HasValue)
        {
            command.Parameters.Add("@Since", SqlDbType.DateTime).Value = parameters.Since.Value.UtcDateTime;
        }

        if (parameters.Until.HasValue)
        {
            command.Parameters.Add("@Until", SqlDbType.DateTime).Value = parameters.Until.Value.UtcDateTime;
        }

        command.Parameters.Add("@Offset", SqlDbType.Int).Value = parameters.Offset;
        command.Parameters.Add("@CountPlusOne", SqlDbType.Int).Value = parameters.Count + 1;
    }

    private SearchEntryResult? TryMapHistoryRow(HistoryRow row)
    {
        try
        {
            var resourceBytes = _compressor.DecompressBytes(row.RawResource);
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize resource {ResourceId} version {Version}", row.ResourceId, row.Version);
            return null;
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
