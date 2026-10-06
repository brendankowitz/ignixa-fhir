namespace Ignixa.PackageManagement.Tests;

internal sealed class StrictInputStream(byte[] bytes, int chunkSize = 17, Action<int>? afterRead = null) : Stream
{
    private int _offset;
    internal bool Disposed { get; private set; }
    internal int BytesRead => _offset;
    public override bool CanRead => !Disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Use asynchronous reads.");
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int count = Math.Min(Math.Min(chunkSize, buffer.Length), bytes.Length - _offset);
        bytes.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        afterRead?.Invoke(_offset);
        return ValueTask.FromResult(count);
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
