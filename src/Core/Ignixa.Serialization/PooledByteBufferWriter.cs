// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;

namespace Ignixa.Serialization;

/// <summary>
/// A contiguous <see cref="IBufferWriter{T}"/> over arrays rented from <see cref="ArrayPool{T}.Shared"/>,
/// which trims retained arrays under memory pressure. Dispose returns the array. Written bytes are
/// cleared before an array goes back to the pool, so resource content does not linger in it.
/// </summary>
internal sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int InitialCapacity = 4096;

    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(InitialCapacity);
    private int _written;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public void Advance(int count) => _written += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Dispose()
    {
        byte[] buffer = _buffer;
        int written = _written;
        _buffer = [];
        _written = 0;
        if (buffer.Length > 0)
        {
            buffer.AsSpan(0, written).Clear();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void EnsureCapacity(int sizeHint)
    {
        long needed = (long)_written + Math.Max(sizeHint, 1);
        if (needed <= _buffer.Length)
        {
            return;
        }

        if (needed > Array.MaxLength)
        {
            throw new InvalidOperationException(
                $"The serialized JSON needs {needed} bytes, which exceeds the largest buffer of {Array.MaxLength} bytes.");
        }

        byte[] larger = ArrayPool<byte>.Shared.Rent((int)Math.Min(Math.Max(needed, _buffer.Length * 2L), Array.MaxLength));
        Span<byte> written = _buffer.AsSpan(0, _written);
        written.CopyTo(larger);
        written.Clear();
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = larger;
    }
}
