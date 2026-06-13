// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Ignixa.SqlOnFhir.Writers;

/// <summary>
/// Writes rows to a Parquet file on the local file system.
/// Buffers rows in memory and writes them in batches for efficiency.
/// </summary>
public partial class ParquetFileWriter : IAsyncDisposable
{
    private readonly string _outputPath;
    private readonly ILogger _logger;
    private readonly ParquetSchema _schema;
    private readonly Dictionary<string, string> _columnTypeMap;
    private readonly int _rowsPerBatch;
    private readonly List<Dictionary<string, object?>> _rowBuffer;
    private FileStream? _fileStream;
    private ParquetWriter? _parquetWriter;
    private bool _disposed;

    /// <summary>
    /// Default number of rows to buffer before writing a Parquet row group.
    /// </summary>
    public const int DefaultRowsPerBatch = 10_000;

    public long BytesWritten { get; private set; }

    public ParquetFileWriter(
        string outputPath,
        ParquetSchema schema,
        ILogger logger,
        Dictionary<string, string>? columnTypeMap = null,
        int rowsPerBatch = DefaultRowsPerBatch)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(logger);

        if (rowsPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowsPerBatch), "Rows per batch must be greater than zero");
        }

        _outputPath = outputPath;
        _schema = schema;
        _logger = logger;
        _columnTypeMap = columnTypeMap ?? new Dictionary<string, string>();
        _rowsPerBatch = rowsPerBatch;
        _rowBuffer = new List<Dictionary<string, object?>>(_rowsPerBatch);
    }

    /// <summary>
    /// Writes a single row to the Parquet file.
    /// Rows are buffered and written in batches.
    /// </summary>
    public async Task WriteRowAsync(Dictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _rowBuffer.Add(row);

        // Flush batch if buffer is full
        if (_rowBuffer.Count >= _rowsPerBatch)
        {
            await FlushBatchAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Writes multiple rows to the Parquet file.
    /// </summary>
    public async Task WriteRowsAsync(IEnumerable<Dictionary<string, object?>> rows, CancellationToken cancellationToken = default)
    {
        foreach (var row in rows)
        {
            await WriteRowAsync(row, cancellationToken);
        }
    }

    /// <summary>
    /// Flushes all buffered rows and finalizes the Parquet file.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // Flush any remaining rows in buffer
            if (_rowBuffer.Count > 0)
            {
                await FlushBatchAsync(cancellationToken);
            }

            // Finalize the Parquet file
            if (_parquetWriter != null)
            {
                await _parquetWriter.DisposeAsync();
                _parquetWriter = null;
            }

            if (_fileStream != null)
            {
                BytesWritten = _fileStream.Length;
                await _fileStream.DisposeAsync();
                _fileStream = null;

                LogParquetFileWritten(_logger, BytesWritten, _outputPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing Parquet file to: {OutputPath}", _outputPath);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Parquet writer for: {OutputPath}", _outputPath);
            throw;
        }
        finally
        {
            if (_parquetWriter != null)
            {
                await _parquetWriter.DisposeAsync();
            }

            _fileStream?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        if (_rowBuffer.Count == 0)
        {
            return;
        }

        try
        {
            // Initialize writer on first batch
            if (_parquetWriter == null)
            {
                await InitializeWriterAsync(cancellationToken);
            }

            // Write row group
            using var groupWriter = _parquetWriter!.CreateRowGroup();

            // Write all columns defined in the schema
            foreach (var field in _schema.Fields)
            {
                await WriteColumnAsync(groupWriter, field.Name, cancellationToken);
            }

            LogParquetRowGroupWritten(_logger, _rowBuffer.Count);

            // Clear buffer for next batch
            _rowBuffer.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing Parquet batch to: {OutputPath}", _outputPath);
            throw;
        }
    }

    private async Task InitializeWriterAsync(CancellationToken cancellationToken)
    {
        // Create file stream
        _fileStream = File.Create(_outputPath);

        // Create Parquet writer
        _parquetWriter = await ParquetWriter.CreateAsync(_schema, _fileStream, cancellationToken: cancellationToken);

        LogParquetWriterInitialized(_logger, _outputPath);
    }

    private async Task WriteColumnAsync(ParquetRowGroupWriter groupWriter, string columnName, CancellationToken cancellationToken)
    {
        // Get the field from schema
        var field = _schema.Fields.First(f => f.Name == columnName);

        // Extract values from the row buffer
        var rawValues = _rowBuffer
            .Select(row => row.TryGetValue(columnName, out var value) ? value : null)
            .ToArray();

        // Determine SQL type from the column type map
        var sqlType = _columnTypeMap.TryGetValue(columnName, out var type)
            ? type.ToUpperInvariant()
            : "STRING";

        // Write column based on SQL type
        await WriteColumnBySqlTypeAsync(groupWriter, field, columnName, rawValues, sqlType, cancellationToken);
    }

    private async Task WriteColumnBySqlTypeAsync(
        ParquetRowGroupWriter groupWriter,
        Field field,
        string columnName,
        object?[] rawValues,
        string sqlType,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (sqlType)
            {
                case "STRING":
                {
                    var dataField = (DataField<string>)field;
                    var stringValues = rawValues.Select(v => v?.ToString()).ToArray();
                    var column = new DataColumn(dataField, stringValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                    break;
                }

                case "BOOLEAN":
                {
                    var dataField = (DataField<bool?>)field;
                    var boolValues = rawValues.Select(v => v switch
                    {
                        null => (bool?)null,
                        bool b => b,
                        _ => bool.TryParse(v.ToString(), out var result) ? result : (bool?)null
                    }).ToArray();
                    var column = new DataColumn(dataField, boolValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                    break;
                }

                case "DATE":
                {
                    var dataField = (DataField<DateTime?>)field;
                    var dateTimeValues = rawValues.Select(v => v switch
                    {
                        null => (DateTime?)null,
                        DateTime dt => dt,
                        _ => DateTime.TryParse(v.ToString(), out var result) ? result : (DateTime?)null
                    }).ToArray();
                    var column = new DataColumn(dataField, dateTimeValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                    break;
                }

                case "DATETIME":
                {
                    // Parquet.Net dropped DateTimeOffset support: "DateTimeOffset support was dropped
                    // due to numerous ambiguity issues, please use DateTime from now on."
                    // See: https://github.com/aloneguid/parquet-dotnet/issues/294
                    var dataField = (DataField<DateTime?>)field;
                    var dateTimeValues = rawValues.Select(v => v switch
                    {
                        null => (DateTime?)null,
                        DateTimeOffset dto => dto.UtcDateTime,
                        DateTime dt => dt,
                        _ => DateTime.TryParse(v.ToString(), out var result) ? result : (DateTime?)null
                    }).ToArray();
                    var column = new DataColumn(dataField, dateTimeValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                    break;
                }

                case "INTEGER":
                {
                    var dataField = (DataField<int?>)field;
                    var intValues = rawValues.Select(v => v switch
                    {
                        null => (int?)null,
                        int i => i,
                        _ => int.TryParse(v.ToString(), out var result) ? result : (int?)null
                    }).ToArray();
                    var column = new DataColumn(dataField, intValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                    break;
                }

                case "DECIMAL":
                {
                    var dataField = (DataField<decimal?>)field;
                    var decimalValues = rawValues.Select(v => v switch
                    {
                        null => (decimal?)null,
                        decimal d => d,
                        _ => decimal.TryParse(v.ToString(), out var result) ? result : (decimal?)null
                    }).ToArray();
                    var column = new DataColumn(dataField, decimalValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                    break;
                }

                default:
                {
                    // Unknown type - convert to string
                    var dataField = (DataField<string>)field;
                    var stringValues = rawValues.Select(v => v?.ToString()).ToArray();
                    var column = new DataColumn(dataField, stringValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error writing column {ColumnName} as type {SqlType}. Parquet schema is immutable and cannot fallback to different type.",
                columnName,
                sqlType);
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType());
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote Parquet file ({BytesWritten} bytes) to: {OutputPath}")]
    private static partial void LogParquetFileWritten(ILogger logger, long bytesWritten, string outputPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote Parquet row group with {RowCount} rows")]
    private static partial void LogParquetRowGroupWritten(ILogger logger, int rowCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Initialized Parquet writer for file: {OutputPath}")]
    private static partial void LogParquetWriterInitialized(ILogger logger, string outputPath);
}
