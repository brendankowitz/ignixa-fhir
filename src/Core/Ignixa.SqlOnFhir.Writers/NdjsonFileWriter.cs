using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ignixa.SqlOnFhir.Writers;

public partial class NdjsonFileWriter : IAsyncDisposable
{
    private readonly string _outputPath;
    private readonly ByteCountingStream? _stream;
    private readonly ILogger _logger;
    private StreamWriter? _writer;
    private bool _disposed;
    private long _rowsWritten;

    public long BytesWritten { get; private set; }
    public long RowsWritten => _rowsWritten;

    public NdjsonFileWriter(string outputPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(logger);
        _outputPath = outputPath;
        _logger = logger;
    }

    /// <summary>
    /// Writes to <paramref name="stream"/>, which the caller opened and which the writer owns from here: it is written, flushed
    /// and disposed by this writer, and never reopened by path.
    /// </summary>
    public NdjsonFileWriter(Stream stream, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(logger);
        _outputPath = "(stream)";
        _stream = new ByteCountingStream(stream);
        _logger = logger;
    }

    public async Task WriteRowAsync(Dictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // No byte order mark: every line of NDJSON must itself be JSON, and a BOM makes the first one not.
        _writer ??= new StreamWriter(_stream ?? (Stream)new FileStream(_outputPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var json = JsonSerializer.Serialize(row);
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        _rowsWritten++;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_writer != null)
        {
            await _writer.FlushAsync(cancellationToken);
            BytesWritten = _stream?.BytesWritten ?? (new FileInfo(_outputPath).Exists ? new FileInfo(_outputPath).Length : 0);
            LogNdjsonFileWritten(_logger, _rowsWritten, BytesWritten, _outputPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var w = _writer;
        _writer = null;
        _disposed = true;
        GC.SuppressFinalize(this);
        if (w != null)
        {
            try { await w.FlushAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error during final flush on dispose"); }
            finally { await w.DisposeAsync(); }
        }

        if (_stream != null)
        {
            await _stream.DisposeAsync();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, GetType());

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote NDJSON file with {RowsWritten} rows ({BytesWritten} bytes) to: {OutputPath}")]
    private static partial void LogNdjsonFileWritten(ILogger logger, long rowsWritten, long bytesWritten, string outputPath);
}
