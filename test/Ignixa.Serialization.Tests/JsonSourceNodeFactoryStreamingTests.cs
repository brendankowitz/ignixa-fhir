// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

public class JsonSourceNodeFactoryStreamingTests
{
    private const string BundleJson = """
        {
          "resourceType": "Bundle",
          "type": "transaction-response",
          "meta": { "lastUpdated": "2026-10-07T00:00:00.000+00:00" },
          "entry": [
            { "response": { "status": "201 Created", "location": "Patient/a/_history/1" },
              "resource": { "resourceType": "Patient", "id": "a", "name": [ { "family": "O'Brien \"Jr\" <b>é☃" } ], "birthDate": "1970-01-01" } },
            { "response": { "status": "200 OK" }, "resource": null },
            { "response": { "status": "204 No Content" } }
          ],
          "link": [ { "relation": "self", "url": "http://localhost/?a=b&c=d" } ],
          "total": 3.0
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAResource_WhenSerializingToAStreamAsync_ThenOutputMatchesSerializeToString(bool pretty)
    {
        // Arrange
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(BundleJson);
        using var stream = new MemoryStream();

        // Act
        await bundle.SerializeToStreamAsync(stream, pretty);

        // Assert
        Encoding.UTF8.GetString(stream.ToArray()).ShouldBe(bundle.SerializeToString(pretty));
    }

    [Fact]
    public async Task GivenALargeEntryArray_WhenSerializingToAStreamAsync_ThenBytesReachTheStreamBeforeTheResourceIsComplete()
    {
        // Arrange: ~2 MB of entries; the whole response must never sit in one buffer before the first write.
        var entries = string.Join(",", Enumerable.Range(0, 200).Select(i =>
            "{\"resource\":{\"resourceType\":\"Basic\",\"id\":\"b" + i + "\",\"text\":{\"status\":\"generated\",\"div\":\"<div>" + new string('x', 10_000) + "</div>\"}}}"));
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            $$"""{"resourceType":"Bundle","type":"transaction-response","entry":[{{entries}}]}""");
        using var stream = new WriteRecordingStream();

        // Act
        await bundle.SerializeToStreamAsync(stream);

        // Assert
        stream.WriteSizes.Count.ShouldBeGreaterThan(1);
        stream.WriteSizes.Max().ShouldBeLessThan(512 * 1024);
        Encoding.UTF8.GetString(stream.ToArray()).ShouldBe(bundle.SerializeToString());
    }

    private sealed class WriteRecordingStream : MemoryStream
    {
        public List<int> WriteSizes { get; } = [];

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteSizes.Add(count);
            base.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteSizes.Add(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            WriteSizes.Add(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }
    }
}
