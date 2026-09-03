using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Search.Sql.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.EventStore;

/// <summary>
/// Raw-ADO.NET <see cref="ISourceEventStore"/> over <see cref="ISqlExecutionService"/>, replacing the
/// EF implementation. Behaviour is preserved exactly, including two things that read as accidents but are
/// load-bearing: every event in one <see cref="AppendAsync"/> call shares a single timestamp and a single
/// transaction-id cutoff, and the returned events carry <c>TransactionId = 0</c> even though the rows just
/// written carry the real cutoff (only the read path surfaces it).
/// </summary>
public sealed class SqlServerSourceEventStore(
    ISqlExecutionService sqlExecutionService,
    int tenantId,
    ILogger<SqlServerSourceEventStore> logger) : ISourceEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly TableDescriptor Events = SqlCatalog.Default.Table("SourceEvents");

    // Timestamp is a SQL Server keyword (the deprecated rowversion synonym), so it must stay bracketed
    // wherever it appears -- unbracketed it parses as the type, not this column.
    private static readonly string SelectColumns =
        $"{Events.Column("EventId").Name}, {Events.Column("StreamId").Name}, {Events.Column("EventType").Name}, " +
        $"{Events.Column("EventData").Name}, [{Events.Column("Timestamp").Name}], {Events.Column("TransactionId").Name}";

    private static readonly string QualifiedTable = $"{Events.SchemaName}.{Events.TableName}";

    public async Task<IReadOnlyList<SourceEvent>> AppendAsync(IEnumerable<NewSourceEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        var eventsList = events.ToList();
        if (eventsList.Count == 0)
        {
            return [];
        }

        var timestamp = DateTimeOffset.UtcNow;
        var currentTransactionId = await ReadVisibleTransactionCutoffAsync(cancellationToken);

        var eventIds = await InsertAsync(eventsList, timestamp, currentTransactionId, cancellationToken);

        logger.LogInformation(
            "Appended {Count} events to event store (TransactionId cutoff: {TransactionId})",
            eventsList.Count,
            currentTransactionId);

        return eventIds
            .Select((eventId, i) => new SourceEvent(
                eventId,
                eventsList[i].StreamId,
                eventsList[i].EventType,
                eventsList[i].Data,
                timestamp))
            .ToList();
    }

    public IAsyncEnumerable<SourceEvent> ReadAllAsync(CancellationToken cancellationToken)
        => QueryAsync($"SELECT {SelectColumns} FROM {QualifiedTable} ORDER BY {Events.Column("EventId").Name}",
            _ => { },
            cancellationToken);

    public IAsyncEnumerable<SourceEvent> ReadFromAsync(long afterEventId, CancellationToken cancellationToken)
        => QueryAsync(
            $"SELECT {SelectColumns} FROM {QualifiedTable} WHERE {Events.Column("EventId").Name} > @afterEventId " +
            $"ORDER BY {Events.Column("EventId").Name}",
            command => command.Parameters.AddWithValue("@afterEventId", afterEventId),
            cancellationToken);

    public IAsyncEnumerable<SourceEvent> ReadStreamAsync(string streamId, CancellationToken cancellationToken)
        => QueryAsync(
            $"SELECT {SelectColumns} FROM {QualifiedTable} WHERE {Events.Column("StreamId").Name} = @streamId " +
            $"ORDER BY {Events.Column("EventId").Name}",
            command => command.Parameters.AddWithValue("@streamId", streamId),
            cancellationToken);

    private async Task<long> ReadVisibleTransactionCutoffAsync(CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            "SELECT MAX(SurrogateIdRangeFirstValue) FROM dbo.Transactions WHERE IsVisible = 1");

        var values = await sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            reader => reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0),
            cancellationToken);

        return values.Count > 0 ? values[0] ?? 0L : 0L;
    }

    private async Task<IReadOnlyList<long>> InsertAsync(
        IReadOnlyList<NewSourceEvent> eventsList,
        DateTimeOffset timestamp,
        long currentTransactionId,
        CancellationToken cancellationToken)
    {
        var sql = new StringBuilder()
            .Append("INSERT INTO ").Append(QualifiedTable)
            .Append(" (").Append(Events.Column("StreamId").Name)
            .Append(", ").Append(Events.Column("EventType").Name)
            .Append(", ").Append(Events.Column("EventData").Name)
            .Append(", [").Append(Events.Column("Timestamp").Name)
            .Append("], ").Append(Events.Column("TransactionId").Name).Append(')')
            .Append(" OUTPUT INSERTED.").Append(Events.Column("EventId").Name)
            .Append(" SELECT s.StreamId, s.EventType, s.EventData, @timestamp, @transactionId FROM (VALUES ");

        using var command = new SqlCommand();
        for (var i = 0; i < eventsList.Count; i++)
        {
            var ordinal = i.ToString(CultureInfo.InvariantCulture);
            if (i > 0)
            {
                sql.Append(", ");
            }

            sql.Append('(').Append(ordinal)
                .Append(", @streamId").Append(ordinal)
                .Append(", @eventType").Append(ordinal)
                .Append(", @eventData").Append(ordinal).Append(')');

            command.Parameters.AddWithValue($"@streamId{ordinal}", eventsList[i].StreamId);
            command.Parameters.AddWithValue($"@eventType{ordinal}", eventsList[i].EventType);
            command.Parameters.AddWithValue($"@eventData{ordinal}", JsonSerializer.Serialize(eventsList[i].Data, JsonOptions));
        }

        // ORDER BY the source ordinal so IDENTITY values are assigned in input order. OUTPUT makes no
        // guarantee about the order of its own result set, so the ids are sorted ascending below and zipped
        // back onto the input -- which is what reproduces EF's positional entity-to-identity correlation.
        sql.Append(") AS s(Ord, StreamId, EventType, EventData) ORDER BY s.Ord");

        command.Parameters.AddWithValue("@timestamp", timestamp);
        command.Parameters.AddWithValue("@transactionId", currentTransactionId);

        // CA2100 suppressed: every value flows through a parameter. The only interpolated fragments are
        // catalog-sourced identifiers and loop ordinals, never caller input.
#pragma warning disable CA2100
        command.CommandText = sql.ToString();
#pragma warning restore CA2100

        // NonIdempotent: this is an unguarded INSERT that comes through ExecuteReaderAsync only because it
        // needs the generated EventIds back. A -2 command timeout does not prove the server did not commit
        // it, so a retry would append the whole batch a second time -- duplicate events in a stream are
        // worse than a surfaced failure the caller can retry with its own idempotency.
        var eventIds = await sqlExecutionService.ExecuteReaderAsync(
            tenantId, command, reader => reader.GetInt64(0), cancellationToken, SqlCommandIdempotency.NonIdempotent);

        return [.. eventIds.OrderBy(id => id)];
    }

    private async IAsyncEnumerable<SourceEvent> QueryAsync(
        string sql,
        Action<SqlCommand> configureParameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ExecuteReaderAsync fully materializes, so this yields from an already-fetched list rather than a
        // live cursor -- same shape as SqlServerHistoryQueryExecutor, and identical from the caller's side.
#pragma warning disable CA2100
        using var command = new SqlCommand(sql);
#pragma warning restore CA2100
        configureParameters(command);

        var rows = await sqlExecutionService.ExecuteReaderAsync(tenantId, command, ReadRow, cancellationToken);

        foreach (var row in rows)
        {
            yield return DeserializeEvent(row);
        }
    }

    private static SourceEventRow ReadRow(SqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetDateTimeOffset(4),
        reader.GetInt64(5));

    private SourceEvent DeserializeEvent(SourceEventRow row)
    {
        var dataType = GetEventDataType(row.EventType);

        try
        {
            var data = JsonSerializer.Deserialize(row.EventData, dataType, JsonOptions)
                ?? throw new InvalidOperationException($"Deserialization returned null for EventId {row.EventId}");

            return new SourceEvent(row.EventId, row.StreamId, row.EventType, data, row.Timestamp, row.TransactionId);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize event {EventId} of type {EventType}", row.EventId, row.EventType);
            throw new InvalidOperationException(
                $"Failed to deserialize event {row.EventId} of type '{row.EventType}'. " +
                $"This may indicate database corruption or version mismatch.", ex);
        }
    }

    private static Type GetEventDataType(string eventType) => eventType switch
    {
        nameof(PackageUploaded) => typeof(PackageUploaded),
        nameof(PackageActivated) => typeof(PackageActivated),
        nameof(PackageDeactivated) => typeof(PackageDeactivated),
        nameof(SearchParameterActivated) => typeof(SearchParameterActivated),
        nameof(SearchParameterReindexStarted) => typeof(SearchParameterReindexStarted),
        nameof(SearchParameterReindexCompleted) => typeof(SearchParameterReindexCompleted),
        nameof(SearchParameterReindexFailed) => typeof(SearchParameterReindexFailed),
        nameof(SearchParameterDeactivated) => typeof(SearchParameterDeactivated),
        nameof(SearchParameterDeleted) => typeof(SearchParameterDeleted),
        nameof(StructureDefinitionActivated) => typeof(StructureDefinitionActivated),
        nameof(StructureDefinitionDeactivated) => typeof(StructureDefinitionDeactivated),
        _ => throw new InvalidOperationException(
            $"Unknown event type '{eventType}'. This may indicate database corruption or version mismatch. " +
            $"Valid types: PackageUploaded, PackageActivated, PackageDeactivated, SearchParameterActivated, " +
            $"SearchParameterReindexStarted, SearchParameterReindexCompleted, SearchParameterReindexFailed, " +
            $"SearchParameterDeactivated, SearchParameterDeleted, StructureDefinitionActivated, StructureDefinitionDeactivated"),
    };

    private sealed record SourceEventRow(
        long EventId,
        string StreamId,
        string EventType,
        string EventData,
        DateTimeOffset Timestamp,
        long TransactionId);
}
