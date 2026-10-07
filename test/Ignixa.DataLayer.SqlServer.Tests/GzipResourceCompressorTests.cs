// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.Serialization.SourceNodes;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class GzipResourceCompressorTests
{
    private const string PatientJson = """{"resourceType":"Patient","id":"p1","name":[{"family":"Chalmers"}]}""";

    private readonly GzipResourceCompressor _compressor = new(new RecyclableMemoryStreamManager());

    [Fact]
    public void GivenCompressedResource_WhenDecompressing_ThenRoundTripsTheJson()
    {
        // Arrange
        byte[] compressed = _compressor.SerializeAndCompress(ResourceJsonNode.Parse(PatientJson));

        // Act
        ReadOnlyMemory<byte> decompressed = _compressor.DecompressBytes(compressed);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(decompressed.Span), JsonNode.Parse(PatientJson)).ShouldBeTrue();
    }

    [Fact]
    public void GivenCompressedResourceSlicedFromALargerBuffer_WhenDecompressing_ThenReadsOnlyTheSlice()
    {
        // Arrange
        byte[] compressed = _compressor.SerializeAndCompress(ResourceJsonNode.Parse(PatientJson));
        byte[] padded = [0xFF, 0xFF, .. compressed, 0xFF];
        ReadOnlyMemory<byte> slice = padded.AsMemory(2, compressed.Length);

        // Act
        ReadOnlyMemory<byte> decompressed = _compressor.DecompressBytes(slice);

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(decompressed.Span), JsonNode.Parse(PatientJson)).ShouldBeTrue();
    }

    [Fact]
    public void GivenEmptyInput_WhenDecompressing_ThenReturnsEmpty()
    {
        _compressor.DecompressBytes(ReadOnlyMemory<byte>.Empty).Length.ShouldBe(0);
    }

    [Fact]
    public void GivenDecompression_WhenInputArrayIsReused_ThenResultIsAnIndependentCopy()
    {
        // Arrange
        byte[] compressed = _compressor.SerializeAndCompress(ResourceJsonNode.Parse(PatientJson));

        // Act
        ReadOnlyMemory<byte> decompressed = _compressor.DecompressBytes(compressed);
        Array.Clear(compressed);

        // Assert
        Encoding.UTF8.GetString(decompressed.Span).ShouldContain("Chalmers");
    }
}
