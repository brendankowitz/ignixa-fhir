// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Output;

/// <summary>
/// The output file as the writers see it. The writers dispose the stream they are given when they finish (Parquet already in its
/// flush); disposing this one flushes the file but leaves it open, because <see cref="OutputFile"/> asks the operating system where
/// the open file is just before it renames it, and closes it itself.
/// </summary>
internal sealed class UnclosedStream(FileStream file) : Stream
{
    private bool _disposed;

    public override bool CanRead => file.CanRead;

    public override bool CanSeek => file.CanSeek;

    public override bool CanWrite => file.CanWrite;

    public override long Length => file.Length;

    public override long Position
    {
        get => file.Position;
        set => file.Position = value;
    }

    public override void Flush() => file.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => file.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => file.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => file.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => file.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => file.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => file.Seek(offset, origin);

    public override void SetLength(long value) => file.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => file.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => file.Write(buffer);

    public override void WriteByte(byte value) => file.WriteByte(value);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => file.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => file.WriteAsync(buffer, cancellationToken);

    // Disposing twice is allowed of a stream; only the first flushes, since the file may be closed by then.
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await file.FlushAsync();
        }

        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            file.Flush();
        }

        base.Dispose(disposing);
    }
}
