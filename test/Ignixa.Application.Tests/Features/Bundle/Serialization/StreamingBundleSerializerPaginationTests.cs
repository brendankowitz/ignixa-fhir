// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using System.Text.Json;
using Shouldly;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Tests for StreamingBundleSerializer.SerializeWithPaginationAsync.
/// Specifically tests the pagination pattern with pageSize+1 results and _include/_revinclude handling.
/// </summary>
public class StreamingBundleSerializerPaginationTests
{
    private const int PageSize = 10;
    private const string BaseUrl = "http://localhost:5000/Patient";
    private const string QueryString = "?_count=10";
    private static readonly string[] OrganizationIds = new[] { "organization-100", "organization-101", "organization-102" };
    private static readonly string[] ObservationIds = new[] { "observation-200", "observation-201" };

    [Fact]
    public async Task SerializeWithPaginationAsync_WithPageSizeExactly_NoNextLink()
    {
        // Arrange: Create exactly pageSize Match entries (no +1 indicator)
        var entries = CreateMatchEntries(count: PageSize);
        var searchOptions = new SearchOptions { MaxItemCount = PageSize };

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            null,
            CreateAsyncEnumerable(entries),
            searchOptions,
            BaseUrl,
            QueryString);

        // Assert
        var result = ParseBundleResponse(outputStream);
        result.EntryCount.ShouldBe(PageSize);
        result.HasNextLink.ShouldBeFalse();
    }

    [Fact]
    public async Task SerializeWithPaginationAsync_WithPageSizePlusOne_RendersPagesizeAndHasNextLink()
    {
        // Arrange: Create pageSize + 1 Match entries
        // The +1 entry should be detected as "hasMore" indicator but not rendered
        var entries = CreateMatchEntries(count: PageSize + 1);
        var searchOptions = new SearchOptions { MaxItemCount = PageSize };

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            null,
            CreateAsyncEnumerable(entries),
            searchOptions,
            BaseUrl,
            QueryString);

        // Assert
        var result = ParseBundleResponse(outputStream);

        // Should render exactly pageSize entries (not the +1)
        result.EntryCount.ShouldBe(PageSize);

        // Should have a next link since there are more results
        result.HasNextLink.ShouldBeTrue();

        // The +1 entry (Patient-11) should not be in the response
        result.ResourceIds.ShouldNotContain("patient-11");
    }

    [Fact]
    public async Task SerializeWithPaginationAsync_WithIncludesAfterPaging_RendersAllIncludes()
    {
        // Arrange:
        // - pageSize Match entries (Patient resources)
        // - +1 Match entry (indicator)
        // - Multiple Include entries (Organization resources)
        var matchEntries = CreateMatchEntries(count: PageSize + 1, resourceType: "Patient");
        var includeEntries = CreateIncludeEntries(
            count: 5,
            resourceType: "Organization",
            startId: 100);

        var allEntries = matchEntries.Concat(includeEntries).ToList();
        var searchOptions = new SearchOptions { MaxItemCount = PageSize };

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            null,
            CreateAsyncEnumerable(allEntries),
            searchOptions,
            BaseUrl,
            QueryString);

        // Assert
        var result = ParseBundleResponse(outputStream);

        // Should render pageSize Match entries (not the +1 indicator)
        var matchCount = result.Entries.Count(e => e.SearchMode == "match");
        matchCount.ShouldBe(PageSize);

        // Should render ALL Include entries (5 organizations)
        var includeCount = result.Entries.Count(e => e.SearchMode == "include");
        includeCount.ShouldBe(5);

        // Total entries: pageSize Match + 5 Includes
        result.EntryCount.ShouldBe(PageSize + 5);

        // Should have next link (hasMore)
        result.HasNextLink.ShouldBeTrue();

        // All 5 organizations should be present
        for (int i = 0; i < 5; i++)
        {
            result.ResourceIds.ShouldContain($"organization-{100 + i}");
        }
    }

    [Fact]
    public async Task SerializeWithPaginationAsync_WithMixedIncludesAndRevincludes_RendersBoth()
    {
        // Arrange:
        // - pageSize Match entries
        // - +1 Match entry (indicator)
        // - Include entries (Organization)
        // - RevInclude entries (Observation)
        var matchEntries = CreateMatchEntries(count: PageSize + 1, resourceType: "Patient");
        var includeEntries = CreateIncludeEntries(count: 3, resourceType: "Organization", startId: 100);
        var revincludeEntries = CreateIncludeEntries(
            count: 2,
            resourceType: "Observation",
            startId: 200);

        var allEntries = matchEntries.Concat(includeEntries).Concat(revincludeEntries).ToList();
        var searchOptions = new SearchOptions { MaxItemCount = PageSize };

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            null,
            CreateAsyncEnumerable(allEntries),
            searchOptions,
            BaseUrl,
            QueryString);

        // Assert
        var result = ParseBundleResponse(outputStream);

        var matchCount = result.Entries.Count(e => e.SearchMode == "match");
        var includeCount = result.Entries.Count(e => e.SearchMode == "include");

        // pageSize Match entries
        matchCount.ShouldBe(PageSize);

        // 3 Include + 2 RevInclude = 5 total include entries
        includeCount.ShouldBe(5);

        // Total: 10 Match + 5 Include
        result.EntryCount.ShouldBe(PageSize + 5);

        // Verify both Organizations and Observations are present
        var expectedIds = OrganizationIds.Concat(ObservationIds).ToArray();
        foreach (var id in expectedIds)
        {
            result.ResourceIds.ShouldContain(id);
        }
    }

    [Fact]
    public async Task SerializeWithPaginationAsync_WithNoIncludes_OnlyRendersMatches()
    {
        // Arrange: Only Match entries, with +1 indicator
        var entries = CreateMatchEntries(count: PageSize + 1);
        var searchOptions = new SearchOptions { MaxItemCount = PageSize };

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            null,
            CreateAsyncEnumerable(entries),
            searchOptions,
            BaseUrl,
            QueryString);

        // Assert
        var result = ParseBundleResponse(outputStream);

        result.EntryCount.ShouldBe(PageSize);

        // All entries should be matches
        var allMatches = result.Entries.All(e => e.SearchMode == "match");
        allMatches.ShouldBeTrue();

        // Should have next link
        result.HasNextLink.ShouldBeTrue();
    }

    [Fact]
    public async Task SerializeWithPaginationAsync_WithLargeNumberOfIncludes_RendersAll()
    {
        // Arrange: pageSize Match + 1 indicator + many Includes
        var matchEntries = CreateMatchEntries(count: PageSize + 1);
        var includeEntries = CreateIncludeEntries(count: 50, resourceType: "Organization", startId: 100);

        var allEntries = matchEntries.Concat(includeEntries).ToList();
        var searchOptions = new SearchOptions { MaxItemCount = PageSize };

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            null,
            CreateAsyncEnumerable(allEntries),
            searchOptions,
            BaseUrl,
            QueryString);

        // Assert
        var result = ParseBundleResponse(outputStream);

        var matchCount = result.Entries.Count(e => e.SearchMode == "match");
        var includeCount = result.Entries.Count(e => e.SearchMode == "include");

        matchCount.ShouldBe(PageSize);
        includeCount.ShouldBe(50);
        result.EntryCount.ShouldBe(PageSize + 50);
    }

    [Fact]
    public async Task SerializeWithPaginationAsync_WithSmallPageSize_StillRendersAllIncludes()
    {
        // Arrange: Use a very small pageSize (3)
        const int smallPageSize = 3;
        var matchEntries = CreateMatchEntries(count: smallPageSize + 1, resourceType: "Patient");
        var includeEntries = CreateIncludeEntries(count: 10, resourceType: "Organization", startId: 100);

        var allEntries = matchEntries.Concat(includeEntries).ToList();
        var searchOptions = new SearchOptions { MaxItemCount = smallPageSize };

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            outputStream,
            "searchset",
            null,
            CreateAsyncEnumerable(allEntries),
            searchOptions,
            BaseUrl,
            QueryString);

        // Assert
        var result = ParseBundleResponse(outputStream);

        var matchCount = result.Entries.Count(e => e.SearchMode == "match");
        var includeCount = result.Entries.Count(e => e.SearchMode == "include");

        matchCount.ShouldBe(smallPageSize);
        includeCount.ShouldBe(10);
        result.EntryCount.ShouldBe(smallPageSize + 10);
    }

    // Helper methods

    private async IAsyncEnumerable<SearchEntryResult> CreateAsyncEnumerable(List<SearchEntryResult> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            await Task.Delay(0); // Allow async enumeration
        }
    }

    private List<SearchEntryResult> CreateMatchEntries(int count, string resourceType = "Patient")
    {
        var entries = new List<SearchEntryResult>();
#pragma warning disable CA1308
        var lowerResourceType = resourceType.ToLowerInvariant();
#pragma warning restore CA1308
        for (int i = 1; i <= count; i++)
        {
            var resourceJson = $$$"""
            {
              "resourceType": "{{{resourceType}}}",
              "id": "{{{lowerResourceType}}}-{{{i}}}",
              "name": [{"family": "Test{{{i}}}"}]
            }
            """;

            entries.Add(new SearchEntryResult(
                ResourceType: resourceType,
                ResourceId: $"{lowerResourceType}-{i}",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: Encoding.UTF8.GetBytes(resourceJson))
            {
                SearchMode = SearchEntryMode.Match
            });
        }

        return entries;
    }

    private List<SearchEntryResult> CreateIncludeEntries(int count, string resourceType, int startId)
    {
        var entries = new List<SearchEntryResult>();
#pragma warning disable CA1308
        var lowerResourceType = resourceType.ToLowerInvariant();
#pragma warning restore CA1308
        for (int i = 0; i < count; i++)
        {
            var id = startId + i;
            var resourceJson = $$$"""
            {
              "resourceType": "{{{resourceType}}}",
              "id": "{{{lowerResourceType}}}-{{{id}}}",
              "name": "Organization {{{id}}}"
            }
            """;

            entries.Add(new SearchEntryResult(
                ResourceType: resourceType,
                ResourceId: $"{lowerResourceType}-{id}",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: Encoding.UTF8.GetBytes(resourceJson))
            {
                SearchMode = SearchEntryMode.Include
            });
        }

        return entries;
    }

    private BundleParseResult ParseBundleResponse(MemoryStream outputStream)
    {
        outputStream.Position = 0;
        using var document = JsonDocument.Parse(outputStream);
        var root = document.RootElement;

        var entries = new List<EntryInfo>();
        var resourceIds = new List<string>();

        if (root.TryGetProperty("entry", out var entryArray))
        {
            foreach (var entry in entryArray.EnumerateArray())
            {
                var searchMode = "match"; // default
                if (entry.TryGetProperty("search", out var searchObj) &&
                    searchObj.TryGetProperty("mode", out var modeElement))
                {
                    searchMode = modeElement.GetString() ?? "match";
                }

                var fullUrl = entry.TryGetProperty("fullUrl", out var urlElement)
                    ? urlElement.GetString() ?? string.Empty
                    : string.Empty;

                // Extract resource ID from fullUrl (format: "ResourceType/id")
                var resourceId = fullUrl.Split('/').Last();

                entries.Add(new EntryInfo { FullUrl = fullUrl, SearchMode = searchMode });
                resourceIds.Add(resourceId);
            }
        }

        var hasNextLink = false;
        if (root.TryGetProperty("link", out var linkArray))
        {
            foreach (var link in linkArray.EnumerateArray())
            {
                if (link.TryGetProperty("relation", out var relElement) &&
                    relElement.GetString() == "next")
                {
                    hasNextLink = true;
                    break;
                }
            }
        }

        return new BundleParseResult
        {
            EntryCount = entries.Count,
            HasNextLink = hasNextLink,
            Entries = entries,
            ResourceIds = resourceIds
        };
    }

    // Helper classes for test assertions

    private class BundleParseResult
    {
        public int EntryCount { get; set; }
        public bool HasNextLink { get; set; }
        public List<EntryInfo> Entries { get; set; } = new();
        public List<string> ResourceIds { get; set; } = new();
    }

    private class EntryInfo
    {
        public string FullUrl { get; set; } = string.Empty;
        public string SearchMode { get; set; } = string.Empty;
    }

    /// <summary>
    /// Wraps a MemoryStream and records the cumulative byte position each time
    /// the underlying Utf8JsonWriter flushes its buffer (via WriteAsync).
    /// This lets tests assert that bytes reached the stream before all entries
    /// were serialized — i.e., that the serializer streams rather than buffers.
    /// </summary>
    private sealed class FlushTrackingStream : Stream
    {
        private readonly MemoryStream _inner = new();

        /// <summary>Cumulative byte positions recorded at each WriteAsync call.</summary>
        public List<long> WritePositions { get; } = new();

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WritePositions.Add(_inner.Position + count);
            _inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            WritePositions.Add(_inner.Position + buffer.Length);
            await _inner.WriteAsync(buffer, ct);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            WritePositions.Add(_inner.Position + count);
            return _inner.WriteAsync(buffer, offset, count, ct);
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);

        public MemoryStream ToMemoryStream()
        {
            var ms = new MemoryStream(_inner.ToArray());
            return ms;
        }
    }

    /// <summary>
    /// Creates a SearchEntryResult whose ResourceBytes is padded to approximately
    /// <paramref name="approximateSizeBytes"/> by filling the Binary.data field with
    /// repeated characters.  The JSON is valid FHIR Binary so the serializer treats
    /// it identically to a real resource.
    /// </summary>
    private static SearchEntryResult CreateLargeEntry(string id, int approximateSizeBytes)
    {
        // Build a base64-ish data string of roughly the right length.
        // The JSON wrapper is ~80 bytes so we fill the rest with 'A' characters.
        int dataLength = Math.Max(0, approximateSizeBytes - 80);
        var data = new string('A', dataLength);

        var json = $$$"""{"resourceType":"Binary","id":"{{{id}}}","contentType":"application/octet-stream","data":"{{{data}}}"}""";

        return new SearchEntryResult(
            ResourceType: "Binary",
            ResourceId: id,
            VersionId: "1",
            LastModified: DateTimeOffset.UtcNow,
            ResourceBytes: Encoding.UTF8.GetBytes(json))
        {
            SearchMode = SearchEntryMode.Match,
        };
    }

    // -------------------------------------------------------------------------
    // Threshold-flush behaviour tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SerializeWithPaginationAsync_WithoutThresholdFlush_BuffersAllBytesUntilEnd()
    {
        // Arrange: 5 entries of ~200 KB each = ~1 MB total.
        // Threshold set impossibly high so no intermediate flush ever fires.
        const int entrySize = 200_000;
        const int entryCount = 5;
        var entries = Enumerable.Range(1, entryCount)
            .Select(i => CreateLargeEntry($"binary-{i}", entrySize))
            .ToList();

        var searchOptions = new SearchOptions { MaxItemCount = entryCount };
        var trackingStream = new FlushTrackingStream();

        // Act: pass int.MaxValue as threshold — no mid-loop flush should occur.
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            trackingStream,
            "searchset",
            null,
            CreateAsyncEnumerable(entries),
            searchOptions,
            BaseUrl,
            QueryString,
            flushThresholdBytes: int.MaxValue);

        // Assert: only one write event — everything was buffered until the final flush.
        trackingStream.WritePositions.Count.ShouldBe(1,
            "with an infinite threshold the writer should flush exactly once at the end");

        // Sanity: JSON must still be valid and contain all entries.
        var result = ParseBundleResponse(trackingStream.ToMemoryStream());
        result.EntryCount.ShouldBe(entryCount);
    }

    [Fact]
    public async Task SerializeWithPaginationAsync_WithThresholdFlush_StreamsBytesBeforeAllEntriesWritten()
    {
        // Arrange: 5 entries of ~200 KB each = ~1 MB total.
        // Threshold set to 400 KB so a flush fires after roughly every 2 entries.
        const int entrySize = 200_000;
        const int entryCount = 5;
        const int threshold = 400_000; // 400 KB — well below total, above single entry

        var entries = Enumerable.Range(1, entryCount)
            .Select(i => CreateLargeEntry($"binary-{i}", entrySize))
            .ToList();

        var searchOptions = new SearchOptions { MaxItemCount = entryCount };
        var trackingStream = new FlushTrackingStream();

        // Act
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            trackingStream,
            "searchset",
            null,
            CreateAsyncEnumerable(entries),
            searchOptions,
            BaseUrl,
            QueryString,
            flushThresholdBytes: threshold);

        // Assert: more than one write event means bytes reached the stream mid-serialization.
        trackingStream.WritePositions.Count.ShouldBeGreaterThan(1,
            "threshold-based flushing should write bytes to the stream before all entries are complete");

        // The first write must have happened before the full response was assembled,
        // confirming the client would have received bytes before serialization finished.
        long firstWritePosition = trackingStream.WritePositions.First();
        long totalBytes = trackingStream.WritePositions.Last();
        firstWritePosition.ShouldBeLessThan(totalBytes,
            "the first flush should deliver a partial response, not the complete bundle");

        // Sanity: JSON must still be valid and contain all entries.
        var result = ParseBundleResponse(trackingStream.ToMemoryStream());
        result.EntryCount.ShouldBe(entryCount);
        result.HasNextLink.ShouldBeFalse();
    }

    [Fact]
    public async Task SerializeWithPaginationAsync_ThresholdFlush_ProducesIdenticalJsonToUnflushedVersion()
    {
        // Prove the fix is purely a streaming optimization — it does not change the
        // content or structure of the bundle JSON.
        const int entrySize = 200_000;
        const int entryCount = 5;

        var entries = Enumerable.Range(1, entryCount)
            .Select(i => CreateLargeEntry($"binary-{i}", entrySize))
            .ToList();

        var searchOptions = new SearchOptions { MaxItemCount = entryCount };

        var bufferedStream = new MemoryStream();
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            bufferedStream, "searchset", null, CreateAsyncEnumerable(entries),
            searchOptions, BaseUrl, QueryString,
            flushThresholdBytes: int.MaxValue);

        var streamingStream = new MemoryStream();
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            streamingStream, "searchset", null, CreateAsyncEnumerable(entries),
            searchOptions, BaseUrl, QueryString,
            flushThresholdBytes: 400_000);

        var bufferedJson = Encoding.UTF8.GetString(bufferedStream.ToArray());
        var streamingJson = Encoding.UTF8.GetString(streamingStream.ToArray());

        streamingJson.ShouldBe(bufferedJson,
            "threshold-based flushing must produce byte-identical output to the unflushed path");
    }
}
