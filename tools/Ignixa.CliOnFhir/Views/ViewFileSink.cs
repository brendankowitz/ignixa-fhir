// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.CliOnFhir.Output;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Parquet.Data;
using Parquet.Schema;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Writes rows to a file with the SQL-on-FHIR writers (<c>Ignixa.SqlOnFhir.Writers</c>, as <c>tools/Ignixa.SqlOnFhir.Cli</c>'s
/// <c>run</c> does). The writers create the file with their first row, so a run with no rows writes none.
/// </summary>
internal sealed class ViewFileSink : IAsyncDisposable
{
    private readonly IReadOnlyList<ColumnSchema> _columns;
    private readonly bool[] _textColumns;
    private readonly Func<Dictionary<string, object?>, CancellationToken, Task> _write;
    private readonly Func<CancellationToken, Task> _flush;
    private readonly Func<long> _bytes;
    private readonly IAsyncDisposable _writer;

    private ViewFileSink(IReadOnlyList<ColumnSchema> columns, bool[] textColumns, Func<Dictionary<string, object?>, CancellationToken, Task> write, Func<CancellationToken, Task> flush, Func<long> bytes, IAsyncDisposable writer)
    {
        _columns = columns;
        _textColumns = textColumns;
        _write = write;
        _flush = flush;
        _bytes = bytes;
        _writer = writer;
    }

    /// <summary>Rows written so far.</summary>
    public long Rows { get; private set; }

    /// <summary>A sink writing <paramref name="format"/> rows to <paramref name="stream"/>, which the writer owns from here (the caller opened it exclusively).</summary>
    public static ViewFileSink Create(Stream stream, string format, IReadOnlyList<ColumnSchema> columns)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(columns);

        if (format == OutputTarget.Csv)
        {
            var csv = new CsvFileWriter(stream, NullLogger.Instance);
            return new ViewFileSink(columns, new bool[columns.Count], csv.WriteRowAsync, csv.FlushAsync, () => csv.BytesWritten, csv);
        }

        if (format == OutputTarget.Ndjson)
        {
            var ndjson = new NdjsonFileWriter(stream, NullLogger.Instance);
            return new ViewFileSink(columns, new bool[columns.Count], ndjson.WriteRowAsync, ndjson.FlushAsync, () => ndjson.BytesWritten, ndjson);
        }

        (ParquetSchema schema, Dictionary<string, string> types) = ParquetSchemaOf(columns);
        var parquet = new ParquetFileWriter(stream, schema, NullLogger.Instance, types);
        bool[] text = [.. columns.Select(column => types[column.Name] == "STRING")];
        return new ViewFileSink(columns, text, parquet.WriteRowAsync, parquet.FlushAsync, () => parquet.BytesWritten, parquet);
    }

    /// <summary>Writes one row, its cells in column order.</summary>
    public async Task WriteAsync(object?[] cells, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cells);

        // In column order, every column present, so a CSV header (the first row's keys) is the schema's.
        Dictionary<string, object?> row = new(_columns.Count, StringComparer.Ordinal);
        for (int i = 0; i < _columns.Count; i++)
        {
            // A Parquet text column takes ToString() in the current culture; the cell is made text in the invariant one first.
            row[_columns[i].Name] = _textColumns[i] && cells[i] is { } cell ? Convert.ToString(cell, CultureInfo.InvariantCulture) : cells[i];
        }

        await _write(row, cancellationToken);
        Rows++;
    }

    /// <summary>Finishes the file and returns its size; 0 when no row was written, so no file exists.</summary>
    public async Task<long> CompleteAsync(CancellationToken cancellationToken)
    {
        await _flush(cancellationToken);
        return Rows == 0 ? 0 : _bytes();
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    // Copied from tools/Ignixa.SqlOnFhir.Cli/Commands/RunCommand.cs (BuildParquetSchema, MapToParquetField), except that only
    // boolean, integer and decimal columns are typed. A collection column holds JSON text whatever its type, and a FHIR date may
    // be partial ("2020-05"): the writer turns a value it cannot parse into a typed column's null without a word, so those
    // columns are text, which keeps every value.
    private static (ParquetSchema Schema, Dictionary<string, string> Types) ParquetSchemaOf(IReadOnlyList<ColumnSchema> columns)
    {
        List<DataField> fields = [];
        Dictionary<string, string> types = new(StringComparer.OrdinalIgnoreCase);
        foreach (ColumnSchema column in columns)
        {
            string sqlType = column.Collection ? "STRING" : (column.Type ?? "STRING").ToUpperInvariant();
            if (sqlType is not ("BOOLEAN" or "INTEGER" or "DECIMAL"))
            {
                sqlType = "STRING";
            }

            fields.Add(sqlType switch
            {
                "BOOLEAN" => new DataField<bool?>(column.Name),
                "INTEGER" => new DataField<int?>(column.Name),
                "DECIMAL" => new DataField<decimal?>(column.Name),
                _ => new DataField<string>(column.Name),
            });
            types[column.Name] = sqlType;
        }

        return (new ParquetSchema(fields), types);
    }
}
