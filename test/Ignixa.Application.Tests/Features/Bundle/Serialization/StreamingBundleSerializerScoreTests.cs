// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Pins <c>Bundle.entry.search.score</c> (Task 8): written immediately after <c>search.mode</c> when
/// <see cref="SearchEntryResult.Score"/> is non-null, and omitted entirely otherwise -- on both entry-
/// writing paths, <see cref="StreamingBundleSerializer.SerializeAsync"/> (no pagination) and
/// <see cref="StreamingBundleSerializer.SerializeWithPaginationAsync"/> (the production search path).
/// </summary>
public class StreamingBundleSerializerScoreTests
{
    [Fact]
    public async Task GivenScore_WhenSerialized_ThenSearchHasModeAndScore()
    {
        var stream = new MemoryStream();

        await StreamingBundleSerializer.SerializeAsync(
            stream, "searchset", total: null, CreateAsyncEnumerable(CreateEntry("p0", score: 0.85)));

        var search = ParseSearchObject(stream);
        search.GetProperty("mode").GetString().ShouldBe("match");
        search.GetProperty("score").GetDouble().ShouldBe(0.85, tolerance: 1e-9);

        // "score" must follow "mode", matching source order (System.Text.Json preserves write order).
        search.EnumerateObject().Select(p => p.Name).ShouldBe(["mode", "score"]);
    }

    [Fact]
    public async Task GivenNoScore_WhenSerialized_ThenSearchHasModeOnly()
    {
        var stream = new MemoryStream();

        await StreamingBundleSerializer.SerializeAsync(
            stream, "searchset", total: null, CreateAsyncEnumerable(CreateEntry("p0", score: null)));

        var search = ParseSearchObject(stream);
        search.TryGetProperty("score", out _).ShouldBeFalse();
        search.EnumerateObject().Select(p => p.Name).ShouldBe(["mode"]);
    }

    [Fact]
    public async Task GivenScore_WhenSerializedWithPagination_ThenSearchHasModeAndScore()
    {
        var stream = new MemoryStream();
        var searchOptions = new SearchOptions { MaxItemCount = 10 };

        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", total: null, CreateAsyncEnumerable(CreateEntry("p0", score: 0.5)),
            searchOptions, "http://localhost/Observation", "?semantic-text=chest+pain");

        var search = ParseSearchObject(stream);
        search.GetProperty("mode").GetString().ShouldBe("match");
        search.GetProperty("score").GetDouble().ShouldBe(0.5, tolerance: 1e-9);
        search.EnumerateObject().Select(p => p.Name).ShouldBe(["mode", "score"]);
    }

    [Fact]
    public async Task GivenNoScore_WhenSerializedWithPagination_ThenSearchHasModeOnly()
    {
        var stream = new MemoryStream();
        var searchOptions = new SearchOptions { MaxItemCount = 10 };

        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", total: null, CreateAsyncEnumerable(CreateEntry("p0", score: null)),
            searchOptions, "http://localhost/Patient", "?name=smith");

        var search = ParseSearchObject(stream);
        search.TryGetProperty("score", out _).ShouldBeFalse();
        search.EnumerateObject().Select(p => p.Name).ShouldBe(["mode"]);
    }

    [Fact]
    public async Task GivenIncludeWithNoScore_WhenSerializedWithPagination_ThenIncludeEntryHasModeIncludeAndNoScore()
    {
        var stream = new MemoryStream();
        var searchOptions = new SearchOptions { MaxItemCount = 10 };
        var match = CreateEntry("obs0", score: 0.9);
        var include = CreateEntry("pt0", score: null) with { SearchMode = SearchEntryMode.Include, ResourceType = "Patient" };

        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", total: null, CreateAsyncEnumerable(match, include),
            searchOptions, "http://localhost/Observation", "?semantic-text=chest+pain&_include=Observation:subject");

        var entries = JsonDocument.Parse(stream.ToArray()).RootElement.GetProperty("entry").EnumerateArray().ToList();
        entries.Count.ShouldBe(2);

        var matchSearch = entries[0].GetProperty("search");
        matchSearch.GetProperty("mode").GetString().ShouldBe("match");
        matchSearch.GetProperty("score").GetDouble().ShouldBe(0.9, tolerance: 1e-9);

        var includeSearch = entries[1].GetProperty("search");
        includeSearch.GetProperty("mode").GetString().ShouldBe("include");
        includeSearch.TryGetProperty("score", out _).ShouldBeFalse();
    }

    private static SearchEntryResult CreateEntry(string id, double? score)
    {
        var resourceJson = $$"""{"resourceType":"Patient","id":"{{id}}"}""";

        return new SearchEntryResult(
            ResourceType: "Patient",
            ResourceId: id,
            VersionId: "1",
            LastModified: DateTimeOffset.UnixEpoch,
            ResourceBytes: Encoding.UTF8.GetBytes(resourceJson))
        {
            SearchMode = SearchEntryMode.Match,
            Score = score,
        };
    }

    private static JsonElement ParseSearchObject(MemoryStream stream) =>
        JsonDocument.Parse(stream.ToArray()).RootElement
            .GetProperty("entry").EnumerateArray().Single()
            .GetProperty("search");

    private static async IAsyncEnumerable<SearchEntryResult> CreateAsyncEnumerable(params SearchEntryResult[] entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            await Task.Yield();
        }
    }
}
