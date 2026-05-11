using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ignixa.SqlOnFhir.Writers;

public partial class NdjsonFileWriter : IAsyncDisposable
{
    private readonly string _outputPath;
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

    public async Task WriteRowAsync(Dictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _writer ??= new StreamWriter(new FileStream(_outputPath, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8);
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
            BytesWritten = new FileInfo(_outputPath).Exists ? new FileInfo(_outputPath).Length : 0;
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
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, GetType());

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote NDJSON file with {RowsWritten} rows ({BytesWritten} bytes) to: {OutputPath}")]
    private static partial void LogNdjsonFileWritten(ILogger logger, long rowsWritten, long bytesWritten, string outputPath);
}
