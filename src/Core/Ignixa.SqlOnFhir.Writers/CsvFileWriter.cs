// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Ignixa.SqlOnFhir.Writers;

/// <summary>
/// Writes rows to a CSV file on the local file system.
/// Writes header row automatically based on first row's keys.
/// </summary>
public class CsvFileWriter : IAsyncDisposable
{
    private readonly string _outputPath;
    private readonly ILogger _logger;
    private StreamWriter? _writer;
    private bool _headerWritten;
    private string[]? _columnNames;
    private bool _disposed;
    private long _rowsWritten;

    public long BytesWritten { get; private set; }
    public long RowsWritten => _rowsWritten;

    public CsvFileWriter(string outputPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(logger);

        _outputPath = outputPath;
        _logger = logger;
    }

    /// <summary>
    /// Writes a single row to the CSV file.
    /// Header is automatically written based on the first row.
    /// </summary>
    public async Task WriteRowAsync(Dictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Initialize writer on first row
        if (_writer == null)
        {
            InitializeWriter();
        }

        // Write header if not yet written
        if (!_headerWritten)
        {
            _columnNames = row.Keys.ToArray();
            await WriteHeaderAsync(_columnNames, cancellationToken);
            _headerWritten = true;
        }

        // Write row values in column order
        var values = _columnNames!.Select(col => row.TryGetValue(col, out var value) ? value : null).ToArray();
        await WriteValuesAsync(values, cancellationToken);
        _rowsWritten++;
    }

    /// <summary>
    /// Writes multiple rows to the CSV file.
    /// </summary>
    public async Task WriteRowsAsync(IEnumerable<Dictionary<string, object?>> rows, CancellationToken cancellationToken = default)
    {
        foreach (var row in rows)
        {
            await WriteRowAsync(row, cancellationToken);
        }
    }

    /// <summary>
    /// Flushes the writer and closes the file.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_writer != null)
        {
            await _writer.FlushAsync(cancellationToken);
            
            // Get file size
            var fileInfo = new FileInfo(_outputPath);
            BytesWritten = fileInfo.Exists ? fileInfo.Length : 0;

            _logger.LogDebug(
                "Wrote CSV file with {RowsWritten} rows ({BytesWritten} bytes) to: {OutputPath}",
                _rowsWritten,
                BytesWritten,
                _outputPath);
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
            _logger.LogWarning(ex, "Error during final flush on dispose");
        }
        finally
        {
            if (_writer != null)
            {
                await _writer.DisposeAsync();
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private void InitializeWriter()
    {
        var fileStream = File.Create(_outputPath);
        _writer = new StreamWriter(fileStream, Encoding.UTF8);

        _logger.LogDebug("Initialized CSV writer for file: {OutputPath}", _outputPath);
    }

    private async Task WriteHeaderAsync(string[] columnNames, CancellationToken cancellationToken)
    {
        var headerLine = string.Join(",", columnNames.Select(EscapeCsvValue));
        await _writer!.WriteLineAsync(headerLine.AsMemory(), cancellationToken);
    }

    private async Task WriteValuesAsync(object?[] values, CancellationToken cancellationToken)
    {
        var valueLine = string.Join(",", values.Select(FormatCsvValue));
        await _writer!.WriteLineAsync(valueLine.AsMemory(), cancellationToken);
    }

    private static string FormatCsvValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        // Format specific types
        var stringValue = value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            double dbl => dbl.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty
        };

        return EscapeCsvValue(stringValue);
    }

    private static string EscapeCsvValue(string value)
    {
        // If value contains comma, newline, or quote, wrap in quotes and escape internal quotes
        if (value.Contains(',', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType());
    }
}
