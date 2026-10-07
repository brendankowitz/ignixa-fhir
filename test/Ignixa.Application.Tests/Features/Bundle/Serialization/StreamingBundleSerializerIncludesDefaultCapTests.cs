// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Tests.Search.Parsing;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// The default include cap end to end: options built from an <c>_include</c> request, serialized over an
/// include stream one row either side of the cap.
/// </summary>
public class StreamingBundleSerializerIncludesDefaultCapTests
{
    private const string BaseUrl = "http://localhost:5000/Patient";
    private const string QueryString = "?_include=Patient:general-practitioner";

    [Fact]
    public async Task GivenOneMoreIncludeThanTheDefaultCap_WhenSerializing_ThenTheCapIsRenderedAndARelatedLinkPointsAtIncludes()
    {
        // Arrange
        int cap = SearchOptionsBuilder.MaxAllowedItemCount;

        // Act
        var (includeCount, relatedLink) = await SerializeAsync(includeCount: cap + 1);

        // Assert
        includeCount.ShouldBe(cap);
        relatedLink.ShouldNotBeNull();
        relatedLink.ShouldContain("/Patient/$includes?");
        relatedLink.ShouldContain("_includesContinuationToken=");
    }

    [Fact]
    public async Task GivenExactlyTheDefaultCapOfIncludes_WhenSerializing_ThenEveryIncludeIsRenderedWithoutARelatedLink()
    {
        // Arrange
        int cap = SearchOptionsBuilder.MaxAllowedItemCount;

        // Act
        var (includeCount, relatedLink) = await SerializeAsync(includeCount: cap);

        // Assert
        includeCount.ShouldBe(cap);
        relatedLink.ShouldBeNull();
    }

    private static async Task<(int IncludeCount, string? RelatedLink)> SerializeAsync(int includeCount)
    {
        SearchOptions options = SearchOptionsBuilderHarness
            .ForPatientChainedThrough("general-practitioner", "Practitioner", "name", SearchParamType.String)
            .Build([("_include", "Patient:general-practitioner")]);
        options.ResourceType.ShouldBe("Patient");

        using var stream = new MemoryStream();
        await StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, Entries(includeCount), options, BaseUrl, QueryString);

        stream.Position = 0;
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        int renderedIncludes = root.GetProperty("entry").EnumerateArray()
            .Count(entry => entry.GetProperty("search").GetProperty("mode").GetString() == "include");
        string? relatedLink = root.GetProperty("link").EnumerateArray()
            .Where(link => link.GetProperty("relation").GetString() == "related")
            .Select(link => link.GetProperty("url").GetString())
            .SingleOrDefault();

        return (renderedIncludes, relatedLink);
    }

    private static async IAsyncEnumerable<SearchEntryResult> Entries(
        int includeCount,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return Entry("Patient", "patient-1", SearchEntryMode.Match);

        for (var i = 0; i < includeCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Entry("Practitioner", $"practitioner-{i}", SearchEntryMode.Include);
        }
    }

    private static SearchEntryResult Entry(string resourceType, string resourceId, SearchEntryMode mode)
        => new(
            ResourceType: resourceType,
            ResourceId: resourceId,
            VersionId: "1",
            LastModified: DateTimeOffset.UnixEpoch,
            ResourceBytes: Encoding.UTF8.GetBytes($$"""{"resourceType":"{{resourceType}}","id":"{{resourceId}}"}"""))
        {
            SearchMode = mode,
        };
}
