// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Text;
using Ignixa.Application.Features.Bundle.Serialization;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

public class FhirJsonWriterBufferTests
{
    [Fact]
    public void GivenABufferWriter_WhenWritingAndFlushing_ThenBytesLandInTheBuffer()
    {
        // Arrange
        var buffer = new ArrayBufferWriter<byte>();

        // Act
        using (var writer = FhirJsonWriter.Create(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("resourceType", "Patient");
            writer.WriteEndObject();
            writer.UnderlyingWriter.Flush();
        }

        // Assert
        Encoding.UTF8.GetString(buffer.WrittenSpan).ShouldBe("""{"resourceType":"Patient"}""");
    }

    [Fact]
    public void GivenAReusedWriterAndBuffer_WhenResetBetweenEntries_ThenNoStateBleedsForward()
    {
        // Arrange
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = FhirJsonWriter.Create(buffer);
        writer.WriteStartObject();
        writer.WriteString("first", "1");
        writer.WriteEndObject();
        writer.UnderlyingWriter.Flush();
        buffer.Clear();
        writer.UnderlyingWriter.Reset(buffer);

        // Act
        writer.WriteStartObject();
        writer.WriteString("second", "2");
        writer.WriteEndObject();
        writer.UnderlyingWriter.Flush();

        // Assert
        Encoding.UTF8.GetString(buffer.WrittenSpan).ShouldBe("""{"second":"2"}""");
    }
}
