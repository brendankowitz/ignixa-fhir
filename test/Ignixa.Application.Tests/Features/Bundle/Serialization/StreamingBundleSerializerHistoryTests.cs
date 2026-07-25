// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using NSubstitute;
using Shouldly;
using FhirBundleLink = Ignixa.Models.BundleLink;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Tests for SerializeHistoryAsync: per-entry buffering with the two-tier recovery keyed on
/// Utf8JsonWriter.BytesCommitted, and the Stu3 bdl-4 conformance branch that suppresses
/// entry.response on history bundles for Stu3 while R4+ requires it.
/// This method flushes on every entry, so tier 2 is the common case from the second entry onward.
/// </summary>
public class StreamingBundleSerializerHistoryTests
{
    private const string ErrorEntryFullUrl = "urn:uuid:00000000-0000-0000-0000-0000000000e0";
    private const string SelfUrlFallback = "_history";

    [Fact]
    public async Task GivenAStu3HistoryBundle_WhenSerializing_ThenNoEntryCarriesAResponseElement()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, TwoEntriesAsync(), links: null, schemaProvider: SchemaFor(FhirVersion.Stu3));

        // Assert
        var entries = ParseEntries(stream);
        entries.Count.ShouldBe(2);
        entries.Count(e => e.TryGetProperty("response", out _)).ShouldBe(0);
        entries.Count(e => e.TryGetProperty("request", out _)).ShouldBe(2);
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenAPostStu3HistoryBundle_WhenSerializing_ThenEveryEntryCarriesAResponseElement(FhirVersion version)
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, TwoEntriesAsync(), links: null, schemaProvider: SchemaFor(version));

        // Assert
        var entries = ParseEntries(stream);
        entries.Count.ShouldBe(2);
        entries.Count(e => e.TryGetProperty("response", out _)).ShouldBe(2);
        entries[0].GetProperty("response").GetProperty("status").GetString().ShouldBe("200");
    }

    [Fact]
    public async Task GivenNoSchemaProvider_WhenSerializingHistory_ThenTheR4ResponseElementIsStillWritten()
    {
        // Arrange -- the parameter is optional and the three production call sites do not yet
        // pass it, so the R4 default must preserve today's output.
        var stream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, TwoEntriesAsync());

        // Assert
        ParseEntries(stream).Count(e => e.TryGetProperty("response", out _)).ShouldBe(2);
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenATierTwoFailureOnAPostStu3Tenant_WhenSerializingHistory_ThenTheErrorEntryCarriesRequestAndResponseOutcome(FhirVersion version)
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, ThrowAfterAsync(1, new InvalidOperationException("boom")),
            links: null, schemaProvider: SchemaFor(version));

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        root.TryGetProperty("link", out _).ShouldBeFalse();

        var errorEntry = root.GetProperty("entry").EnumerateArray().Last();
        errorEntry.GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
        errorEntry.GetProperty("request").GetProperty("method").GetString().ShouldBe("GET");
        errorEntry.GetProperty("response").GetProperty("status").GetString().ShouldBe("500");
        errorEntry.GetProperty("response").GetProperty("outcome")
            .GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("fatal");
        errorEntry.TryGetProperty("resource", out _).ShouldBeFalse();
        errorEntry.TryGetProperty("search", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenATierTwoFailureOnAStu3Tenant_WhenSerializingHistory_ThenTheErrorEntryCarriesResourceAndRequestButNoResponse()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, ThrowAfterAsync(1, new InvalidOperationException("boom")),
            links: null, schemaProvider: SchemaFor(FhirVersion.Stu3));

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
        var errorEntry = JsonDocument.Parse(stream.ToArray()).RootElement
            .GetProperty("entry").EnumerateArray().Last();
        errorEntry.GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
        errorEntry.GetProperty("resource").GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("fatal");
        errorEntry.GetProperty("request").GetProperty("method").GetString().ShouldBe("GET");
        errorEntry.TryGetProperty("response", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenLinksCarryingASelfRelation_WhenATierTwoFailureOccurs_ThenTheErrorEntryRequestUrlIsTheSelfLink()
    {
        // Arrange
        var links = new[] { CreateLink("next", "http://x/Patient/_history?page=2"), CreateLink("self", "http://x/Patient/_history") };

        // Act
        string url = await ErrorEntryRequestUrlAsync(links);

        // Assert
        url.ShouldBe("http://x/Patient/_history");
    }

    [Fact]
    public async Task GivenNullLinks_WhenATierTwoFailureOccurs_ThenTheErrorEntryRequestUrlFallsBackToHistory()
    {
        // Act
        string url = await ErrorEntryRequestUrlAsync(null);

        // Assert
        url.ShouldBe(SelfUrlFallback);
    }

    [Fact]
    public async Task GivenEmptyLinks_WhenATierTwoFailureOccurs_ThenTheErrorEntryRequestUrlFallsBackToHistory()
    {
        // Act
        string url = await ErrorEntryRequestUrlAsync([]);

        // Assert
        url.ShouldBe(SelfUrlFallback);
    }

    [Fact]
    public async Task GivenLinksWithoutASelfRelation_WhenATierTwoFailureOccurs_ThenTheErrorEntryRequestUrlFallsBackToHistory()
    {
        // Arrange
        var links = new[] { CreateLink("next", "http://x/Patient/_history?page=2") };

        // Act
        string url = await ErrorEntryRequestUrlAsync(links);

        // Assert
        url.ShouldBe(SelfUrlFallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GivenASelfLinkWithNoUsableUrl_WhenATierTwoFailureOccurs_ThenTheErrorEntryRequestUrlFallsBackToHistory(string? selfUrl)
    {
        // Arrange
        var links = new[] { CreateLink("self", selfUrl) };

        // Act
        string url = await ErrorEntryRequestUrlAsync(links);

        // Assert
        url.ShouldBe(SelfUrlFallback);
    }

    [Fact]
    public async Task GivenAnEnumeratorThatThrowsBeforeAnyEntry_WhenSerializingHistory_ThenNothingIsWrittenAndTheExceptionPropagates()
    {
        // Arrange -- history flushes per entry, so tier 1 is reachable only before the first entry.
        var stream = new MemoryStream();
        var boom = new InvalidOperationException("boom");

        // Act
        var act = () => StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, ThrowAfterAsync(0, boom));

        // Assert
        (await act.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(boom);
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCancellationBeforeAnyEntry_WhenSerializingHistory_ThenNothingIsWrittenAndCancellationPropagates()
    {
        // Arrange -- history flushes per entry, so tier 1 is reachable only before the first entry,
        // mirroring GivenAnEnumeratorThatThrowsBeforeAnyEntry above but for the cancellation branch.
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, ThrowAfterAsync(0, new OperationCanceledException()));

        // Assert
        await act.ShouldThrowAsync<OperationCanceledException>();
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCorruptResourceBytesAfterACommittedEntry_WhenSerializingHistory_ThenTheBundleIsValidAndCarriesAFatalOutcome()
    {
        // Arrange -- the mid-entry tier-2 case: without per-entry buffering the response writer is
        // left mid-entry and the bundle cannot be closed, producing truncated JSON.
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, EntriesWithCorruptResourceJsonAsync(1));

        // Assert
        await act.ShouldThrowAsync<JsonException>();
        var entries = ParseEntries(stream);
        entries.Count.ShouldBe(2);
        entries[^1].GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
    }

    [Fact]
    public async Task GivenCancellationAfterACommittedEntry_WhenSerializingHistory_ThenTheBundleIsValidWithNoOutcomeEntry()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, ThrowAfterAsync(1, new OperationCanceledException()));

        // Assert
        await act.ShouldThrowAsync<OperationCanceledException>();
        var entries = ParseEntries(stream);
        entries.Count.ShouldBe(1);
        entries[0].GetProperty("fullUrl").GetString().ShouldBe("Patient/p0/_history/1");
    }

    private static async Task<string> ErrorEntryRequestUrlAsync(IReadOnlyList<FhirBundleLink>? links)
    {
        var stream = new MemoryStream();

        var act = () => StreamingBundleSerializer.SerializeHistoryAsync(
            stream, "history", null, ThrowAfterAsync(1, new InvalidOperationException("boom")), links);

        await act.ShouldThrowAsync<InvalidOperationException>();

        return JsonDocument.Parse(stream.ToArray()).RootElement
            .GetProperty("entry").EnumerateArray().Last()
            .GetProperty("request").GetProperty("url").GetString()!;
    }

    private static List<JsonElement> ParseEntries(MemoryStream stream) =>
        JsonDocument.Parse(stream.ToArray()).RootElement.GetProperty("entry").EnumerateArray().ToList();

    private static ISchema SchemaFor(FhirVersion version)
    {
        var schemaProvider = Substitute.For<ISchema>();
        schemaProvider.Version.Returns(version);
        return schemaProvider;
    }

    private static FhirBundleLink CreateLink(string relation, string? url)
    {
        var link = new FhirBundleLink { Url = url };
        link.SetProperty("relation", JsonValue.Create(relation));
        return link;
    }

    private static SearchEntryResult CreateEntry(string id)
    {
        var resourceJson = $$"""{"resourceType":"Patient","id":"{{id}}","meta":{"versionId":"1"},"name":[{"family":"Test"}]}""";

        return new SearchEntryResult(
            ResourceType: "Patient",
            ResourceId: id,
            VersionId: "1",
            LastModified: DateTimeOffset.UnixEpoch,
            ResourceBytes: Encoding.UTF8.GetBytes(resourceJson))
        {
            SearchMode = SearchEntryMode.Match,
        };
    }

    private static async IAsyncEnumerable<SearchEntryResult> TwoEntriesAsync()
    {
        yield return CreateEntry("p0");
        await Task.Yield();
        yield return CreateEntry("p1");
        await Task.Yield();
    }

    private static async IAsyncEnumerable<SearchEntryResult> ThrowAfterAsync(int count, Exception ex)
    {
        for (var i = 0; i < count; i++)
        {
            yield return CreateEntry($"p{i}");
            await Task.Yield();
        }

        throw ex;
    }

    private static async IAsyncEnumerable<SearchEntryResult> EntriesWithCorruptResourceJsonAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return CreateEntry($"p{i}");
            await Task.Yield();
        }

        yield return CreateEntry("corrupt") with { ResourceBytes = Encoding.UTF8.GetBytes("{\"resourceType\":") };
        await Task.Yield();
    }
}
