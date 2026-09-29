namespace Ignixa.PackageManagement.Tests;

internal sealed class StrictPendingInputStream(byte[] prefix) : Stream
{
    private readonly TaskCompletionSource<int> _pendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _offset;

    internal Task ReadStarted => _readStarted.Task;
    internal bool Disposed { get; private set; }
    internal int BytesRead => _offset;
    internal void Fail(IOException exception) => _pendingRead.SetException(exception);

    public override bool CanRead => !Disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Use asynchronous reads.");

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset < prefix.Length)
        {
            int count = Math.Min(buffer.Length, prefix.Length - _offset);
            prefix.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return ValueTask.FromResult(count);
        }
        _readStarted.TrySetResult();
        return new(_pendingRead.Task.WaitAsync(cancellationToken));
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        _pendingRead.TrySetCanceled();
        base.Dispose(disposing);
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
