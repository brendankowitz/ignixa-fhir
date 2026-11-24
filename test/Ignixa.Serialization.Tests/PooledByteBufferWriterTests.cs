// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class PooledByteBufferWriterTests
{
    [Fact]
    public void GivenWrittenBytes_WhenDisposed_ThenTheyAreClearedBeforeTheArrayIsReturned()
    {
        var writer = new PooledByteBufferWriter();
        Memory<byte> rented = writer.GetMemory(16);
        rented.Span[..16].Fill(0x7B);
        writer.Advance(16);

        writer.Dispose();

        rented.Span[..16].ToArray().ShouldAllBe(value => value == 0);
    }

    [Fact]
    public void GivenWrittenBytes_WhenTheBufferGrows_ThenTheOutgrownArrayIsClearedAndTheBytesAreKept()
    {
        using var writer = new PooledByteBufferWriter();
        Memory<byte> rented = writer.GetMemory(16);
        int capacity = rented.Length;
        rented.Span.Fill(0x7B);
        writer.Advance(capacity);

        writer.GetSpan(1);

        rented.Span.ToArray().ShouldAllBe(value => value == 0);
        writer.WrittenSpan.Length.ShouldBe(capacity);
        writer.WrittenSpan.ToArray().ShouldAllBe(value => value == 0x7B);
    }
}
