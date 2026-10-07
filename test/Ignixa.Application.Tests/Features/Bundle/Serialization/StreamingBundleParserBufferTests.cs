// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Pins the parser's buffer management: tokens that do not fit the initial read buffer, header
/// tokens straddling a chunk boundary, and lossless capture of string escapes (issue #473 H1/M1/L2).
/// </summary>
public class StreamingBundleParserBufferTests
{
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(10);

    private readonly StreamingBundleParser _parser = new(NullLogger<StreamingBundleParser>.Instance);

    [Theory]
    [InlineData(20_000)]
    [InlineData(1_000_000)]
    public async Task GivenEntryWithStringLargerThanReadBuffer_WhenParsing_ThenEveryEntryIsYielded(int dataLength)
    {
        var data = new string('A', dataLength);
        var bundleJson = BuildBundle(
            header: string.Empty,
            BinaryEntry("bin-0", "AAAA"),
            BinaryEntry("bin-1", data),
            BinaryEntry("bin-2", "AAAA"));

        var entries = await ParseAllAsync(new MemoryStream(Encoding.UTF8.GetBytes(bundleJson)));

        entries.Select(e => e.Index).ShouldBe([0, 1, 2]);
        JsonDocument.Parse(entries[1].RawJson!).RootElement.GetProperty("data").GetString().ShouldBe(data);
    }

    [Theory]
    [InlineData(5_000, 5_000)]
    [InlineData(20_000, 0)]
    public async Task GivenHeaderTokenCrossingReadBufferBoundary_WhenParsing_ThenHeaderAndEntriesParseWithoutHanging(
        int identifierLength,
        int signatureLength)
    {
        var header = $$"""
            "identifier": { "value": "{{new string('i', identifierLength)}}" },
            "signature": { "data": "{{new string('s', signatureLength)}}" },
            """;
        var bundleJson = BuildBundle(header, BinaryEntry("bin-0", "AAAA"), BinaryEntry("bin-1", "AAAA"));

        // The pre-fix header loop spun without awaiting, so cancellation could never fire; a hang has to be
        // detected from outside the parse rather than with a cancellation token.
        var entries = await Task.Run(() => ParseAllAsync(new MemoryStream(Encoding.UTF8.GetBytes(bundleJson))))
            .WaitAsync(HangTimeout);

        entries.Select(e => e.Index).ShouldBe([0, 1]);
    }

    [Fact]
    public async Task GivenStreamReturningOneByteAtATime_WhenParsing_ThenEveryEntryIsYielded()
    {
        var bundleJson = BuildBundle(
            header: string.Empty,
            BinaryEntry("bin-0", new string('A', 9_000)),
            BinaryEntry("bin-1", "AAAA"));

        var entries = await ParseAllAsync(new OneByteStream(Encoding.UTF8.GetBytes(bundleJson)));

        entries.Select(e => e.Index).ShouldBe([0, 1]);
    }

    [Fact]
    public async Task GivenStringsRequiringEscapes_WhenParsing_ThenRawJsonRoundTripsEveryValue()
    {
        // Control characters other than \n \r \t, a quote and backslash, and a line separator: the
        // re-escaping parser emitted the first group raw, producing JSON the resource parser rejects.
        const string escapedValue = @"ctl:\u0001\b\f quote:\"" slash:\\ sep:\u2028 e:\u00e9";
        var bundleJson = BuildBundle(
            header: string.Empty,
            $$"""
            {
              "resource": { "resourceType": "Basic", "id": "b1", "code": { "text": "{{escapedValue}}" } },
              "request": { "method": "PUT", "url": "Basic/b1" }
            }
            """);

        var entries = await ParseAllAsync(new MemoryStream(Encoding.UTF8.GetBytes(bundleJson)));

        var expected = JsonDocument.Parse($"\"{escapedValue}\"").RootElement.GetString();
        entries.Count.ShouldBe(1);
        JsonDocument.Parse(entries[0].RawJson!).RootElement
            .GetProperty("code").GetProperty("text").GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task GivenTokenLargerThanMaximumBuffer_WhenParsing_ThenRequestIsRejectedRatherThanTruncated()
    {
        var parser = new StreamingBundleParser(NullLogger<StreamingBundleParser>.Instance, maxTokenBytes: 64 * 1024);
        var bundleJson = BuildBundle(
            header: string.Empty,
            BinaryEntry("bin-0", "AAAA"),
            BinaryEntry("bin-1", new string('A', 100_000)),
            BinaryEntry("bin-2", "AAAA"));

        var context = await parser.ParseStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes(bundleJson)));
        var yielded = new List<BundleEntryContext>();

        var exception = await Should.ThrowAsync<RequestNotValidException>(async () =>
        {
            await foreach (var entry in context.Entries)
            {
                yielded.Add(entry);
            }
        });

        exception.Message.ShouldContain("65536");
        yielded.Select(e => e.Index).ShouldBe([0]);
    }

    private async Task<List<BundleEntryContext>> ParseAllAsync(Stream stream)
    {
        var context = await _parser.ParseStreamAsync(stream);
        var entries = new List<BundleEntryContext>();
        await foreach (var entry in context.Entries)
        {
            entries.Add(entry);
        }

        return entries;
    }

    private static string BinaryEntry(string id, string data) => $$"""
        {
          "fullUrl": "Binary/{{id}}",
          "resource": { "resourceType": "Binary", "id": "{{id}}", "contentType": "text/plain", "data": "{{data}}" },
          "request": { "method": "PUT", "url": "Binary/{{id}}" }
        }
        """;

    private static string BuildBundle(string header, params string[] entries) => $$"""
        {
          "resourceType": "Bundle",
          "type": "batch",
          {{header}}
          "entry": [ {{string.Join(",", entries)}} ]
        }
        """;

    private sealed class OneByteStream(byte[] content) : MemoryStream(content)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
