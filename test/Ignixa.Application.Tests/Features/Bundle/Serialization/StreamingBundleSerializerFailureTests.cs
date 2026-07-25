// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Tests for mid-stream failure handling in StreamingBundleSerializer: per-entry buffering across
/// SerializeWithPaginationAsync, SerializeAsync, and SerializeStreamAsync.
/// Most tests cover the two-tier recovery keyed on Utf8JsonWriter.BytesCommitted that
/// SerializeWithPaginationAsync and SerializeAsync share: tier 1 (nothing committed) discards the
/// buffer and rethrows so the exception middleware can still produce a status-coded error; tier 2
/// (response started) completes the bundle with a fatal OperationOutcome entry and rethrows.
/// SerializeStreamAsync is the exception (design doc Section 8): it gets per-entry buffering but
/// deliberately has no two-tier recovery -- it never rethrows, because its caller depends on that.
/// </summary>
public class StreamingBundleSerializerFailureTests
{
    private const string WarningEntryFullUrl = "urn:uuid:00000000-0000-0000-0000-0000000000d0";
    private const string ErrorEntryFullUrl = "urn:uuid:00000000-0000-0000-0000-0000000000e0";

    [Fact]
    public async Task GivenAnEnumeratorThatThrowsBeforeAnyFlush_WhenSerializing_ThenNothingIsWrittenAndTheExceptionPropagates()
    {
        // Arrange
        var stream = new MemoryStream();
        var boom = new InvalidOperationException("boom");

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, ThrowAfterAsync(2, boom), NewOptions(), "http://x", "");

        // Assert
        (await act.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(boom);
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAnEnumeratorThatThrowsOnFirstMoveNext_WhenSerializing_ThenNothingIsWrittenAndTheExceptionPropagates()
    {
        // Arrange
        var stream = new MemoryStream();
        var boom = new InvalidOperationException("boom");

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, ThrowAfterAsync(0, boom), NewOptions(), "http://x", "");

        // Assert
        (await act.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(boom);
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAnEnumeratorThatThrowsAfterACommittedFlush_WhenSerializing_ThenTheBundleIsValidAndCarriesAFatalOutcome()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, ThrowAfterAsync(2, new InvalidOperationException("boom")),
            NewOptions(), "http://x", "", flushThresholdBytes: 1);

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        root.GetProperty("entry").EnumerateArray()
            .Any(e => e.TryGetProperty("search", out var s) && s.GetProperty("mode").GetString() == "outcome")
            .ShouldBeTrue();
    }

    [Fact]
    public async Task GivenCorruptResourceBytesBeforeAnyFlush_WhenSerializing_ThenNothingIsWrittenAndTheExceptionPropagates()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, EntriesWithCorruptResourceJsonAsync(2), NewOptions(), "http://x", "");

        // Assert
        await act.ShouldThrowAsync<JsonException>();
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCorruptResourceBytesAfterACommittedFlush_WhenSerializing_ThenTheBundleIsValidAndCarriesAFatalOutcome()
    {
        // Arrange -- the mid-entry tier-2 case: without per-entry buffering the main writer is
        // left mid-entry and closing the bundle is impossible, producing truncated JSON.
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, EntriesWithCorruptResourceJsonAsync(2), NewOptions(), "http://x", "",
            flushThresholdBytes: 1);

        // Assert
        await act.ShouldThrowAsync<JsonException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        var entries = root.GetProperty("entry").EnumerateArray().ToList();
        entries.Count.ShouldBe(3);
        entries[^1].GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
        entries[^1].GetProperty("search").GetProperty("mode").GetString().ShouldBe("outcome");
        entries[^1].GetProperty("resource").GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("fatal");
    }

    [Fact]
    public async Task GivenCancellationBeforeAnyFlush_WhenSerializing_ThenNothingIsWrittenAndCancellationPropagates()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, ThrowAfterAsync(2, new OperationCanceledException()),
            NewOptions(), "http://x", "");

        // Assert
        await act.ShouldThrowAsync<OperationCanceledException>();
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCancellationAfterACommittedFlush_WhenSerializing_ThenTheBundleIsValidWithNoOutcomeEntry()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, ThrowAfterAsync(2, new OperationCanceledException()),
            NewOptions(), "http://x", "", flushThresholdBytes: 1);

        // Assert
        await act.ShouldThrowAsync<OperationCanceledException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        var entries = root.GetProperty("entry").EnumerateArray().ToList();
        entries.Count.ShouldBe(2);
        entries.ShouldAllBe(e => e.GetProperty("search").GetProperty("mode").GetString() == "match");
    }

    [Fact]
    public async Task GivenMoreResultsThanThePageAndATierTwoFailure_WhenSerializing_ThenOnlyTheSelfLinkIsEmitted()
    {
        // Arrange
        var options = NewOptions();
        options.MaxItemCount = 1;
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, ThrowAfterAsync(3, new InvalidOperationException("boom")),
            options, "http://x", "", flushThresholdBytes: 1);

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        root.GetProperty("link").EnumerateArray()
            .Select(l => l.GetProperty("relation").GetString())
            .ShouldBe(["self"]);
    }

    [Fact]
    public async Task GivenANonAbsoluteBaseUrlWithIncludesPendingBeforeAnyFlush_WhenSerializing_ThenNothingIsWrittenAndTheExceptionPropagates()
    {
        // Arrange -- the related-link build calls new Uri(baseUrl, UriKind.Absolute), which the
        // hoist brings inside the guarded region.
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, MatchThenIncludeAsync(), IncludesPendingOptions(), "not-a-url", "");

        // Assert
        await act.ShouldThrowAsync<UriFormatException>();
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenANonAbsoluteBaseUrlWithIncludesPendingAfterACommittedFlush_WhenSerializing_ThenTheBundleIsValidAndCarriesAFatalOutcome()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, MatchThenIncludeAsync(), IncludesPendingOptions(), "not-a-url", "",
            flushThresholdBytes: 1);

        // Assert
        await act.ShouldThrowAsync<UriFormatException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        root.GetProperty("entry").EnumerateArray().Last()
            .GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
    }

    [Fact]
    public async Task GivenATierTwoFailure_WhenSerializingForEachSupportedVersion_ThenTheSearchsetOutcomeEntryShapeIsIdentical()
    {
        foreach (var schemaProvider in new[] { Stu3Schema(), R4Schema(), R5Schema() })
        {
            // Arrange
            var stream = new MemoryStream();

            // Act
            var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
                stream, "searchset", null, ThrowAfterAsync(2, new InvalidOperationException("boom")),
                NewOptions(), "http://x", "", schemaProvider, flushThresholdBytes: 1);

            // Assert
            await act.ShouldThrowAsync<InvalidOperationException>();
            var errorEntry = JsonDocument.Parse(stream.ToArray()).RootElement
                .GetProperty("entry").EnumerateArray().Last();
            errorEntry.GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
            errorEntry.GetProperty("search").GetProperty("mode").GetString().ShouldBe("outcome");
            errorEntry.GetProperty("resource").GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
            errorEntry.TryGetProperty("request", out _).ShouldBeFalse();
            errorEntry.TryGetProperty("response", out _).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task GivenAThrowDuringThePrologue_WhenSerializing_ThenNothingIsWrittenAndTheExceptionPropagates()
    {
        // Arrange -- empty Severity throws inside WriteBundleIssuesPreR5, which Task 2
        // deliberately left unguarded; this fires before any entry, so it must be tier 1.
        var options = NewOptions();
        options.BundleIssues = [new IssueComponent("", "incomplete", Diagnostics: "d")];
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, TwoEntriesAsync(), options, "http://x", "", schemaProvider: R4Schema());

        // Assert
        await act.ShouldThrowAsync<Exception>();
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenWarningIssuesAndATierTwoFailure_WhenSerializing_ThenBothOutcomeEntriesHaveDistinctFullUrls()
    {
        // Arrange
        var options = NewOptions();
        options.BundleIssues = [new IssueComponent("warning", "incomplete", Diagnostics: "d")];
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, ThrowAfterAsync(2, new InvalidOperationException("boom")),
            options, "http://x", "", schemaProvider: R4Schema(), flushThresholdBytes: 1);

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
        var fullUrls = JsonDocument.Parse(stream.ToArray()).RootElement.GetProperty("entry")
            .EnumerateArray()
            .Where(e => e.TryGetProperty("fullUrl", out var f) && f.GetString()!.StartsWith("urn:uuid:", StringComparison.Ordinal))
            .Select(e => e.GetProperty("fullUrl").GetString())
            .ToList();
        fullUrls.ShouldBe([WarningEntryFullUrl, ErrorEntryFullUrl]);
    }

    [Fact]
    public async Task GivenWarningIssuesAndATierTwoFailureOnAnR5Tenant_WhenSerializing_ThenTheBundleCarriesBothTheIssuesPropertyAndTheFatalOutcomeEntry()
    {
        // Arrange
        var options = NewOptions();
        options.BundleIssues = [new IssueComponent("warning", "incomplete", Diagnostics: "d")];
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
            stream, "searchset", null, ThrowAfterAsync(2, new InvalidOperationException("boom")),
            options, "http://x", "", schemaProvider: R5Schema(), flushThresholdBytes: 1);

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;

        root.TryGetProperty("issues", out var issues).ShouldBeTrue();
        issues.GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
        issues.GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("warning");

        var errorEntry = root.GetProperty("entry").EnumerateArray().Last();
        errorEntry.GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
        errorEntry.GetProperty("resource").GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("fatal");
    }

    [Fact]
    public async Task GivenAnEnumeratorThatThrowsBeforeAnyEntry_WhenSerializingSimpleAsync_ThenNothingIsWrittenAndTheExceptionPropagates()
    {
        // Arrange
        var stream = new MemoryStream();
        var boom = new InvalidOperationException("boom");

        // Act
        var act = () => StreamingBundleSerializer.SerializeAsync(
            stream, "searchset", null, ThrowAfterAsync(0, boom));

        // Assert
        (await act.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(boom);
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCorruptResourceBytesOnTheFirstEntry_WhenSerializingSimpleAsync_ThenNothingIsWrittenAndTheExceptionPropagates()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeAsync(
            stream, "searchset", null, EntriesWithCorruptResourceJsonAsync(0));

        // Assert
        await act.ShouldThrowAsync<JsonException>();
        stream.ToArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAnEnumeratorThatThrowsAfterAnEntryIsFlushed_WhenSerializingSimpleAsync_ThenTheBundleIsValidAndCarriesAFatalOutcome()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeAsync(
            stream, "searchset", null, ThrowAfterAsync(1, new InvalidOperationException("boom")));

        // Assert
        await act.ShouldThrowAsync<InvalidOperationException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        var entries = root.GetProperty("entry").EnumerateArray().ToList();
        entries.Count.ShouldBe(2);
        entries[^1].GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
        entries[^1].GetProperty("search").GetProperty("mode").GetString().ShouldBe("outcome");
    }

    [Fact]
    public async Task GivenCorruptResourceBytesAfterAnEntryIsFlushed_WhenSerializingSimpleAsync_ThenTheBundleIsValidAndCarriesAFatalOutcome()
    {
        // Arrange -- SerializeAsync flushes per entry (design §2), so tier 2 is reachable from the
        // second entry onward, same as SerializeHistoryAsync. This fails against a non-buffered
        // implementation: the main writer is left mid-entry and closing the bundle is impossible.
        var stream = new MemoryStream();

        // Act
        var act = () => StreamingBundleSerializer.SerializeAsync(
            stream, "searchset", null, EntriesWithCorruptResourceJsonAsync(1));

        // Assert
        await act.ShouldThrowAsync<JsonException>();
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        var entries = root.GetProperty("entry").EnumerateArray().ToList();
        entries.Count.ShouldBe(2);
        entries[^1].GetProperty("fullUrl").GetString().ShouldBe(ErrorEntryFullUrl);
        entries[^1].GetProperty("resource").GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("fatal");
    }

    private static SearchOptions NewOptions() => new() { MaxItemCount = 10 };

    private static SearchOptions IncludesPendingOptions() => new()
    {
        MaxItemCount = 10,
        IncludesMaxItemCount = 0,
        ResourceType = "Patient",
    };

    private static ISchema Stu3Schema() => SchemaFor(FhirVersion.Stu3);

    private static ISchema R4Schema() => SchemaFor(FhirVersion.R4);

    private static ISchema R5Schema() => SchemaFor(FhirVersion.R5);

    private static ISchema SchemaFor(FhirVersion version)
    {
        var schemaProvider = Substitute.For<ISchema>();
        schemaProvider.Version.Returns(version);
        return schemaProvider;
    }

    private static SearchEntryResult CreateEntry(string id)
    {
        var resourceJson = $$"""{"resourceType":"Patient","id":"{{id}}","name":[{"family":"Test"}]}""";

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

    private static async IAsyncEnumerable<SearchEntryResult> TwoEntriesAsync()
    {
        yield return CreateEntry("p0");
        await Task.Yield();
        yield return CreateEntry("p1");
        await Task.Yield();
    }

    private static async IAsyncEnumerable<SearchEntryResult> MatchThenIncludeAsync()
    {
        yield return CreateEntry("p0");
        await Task.Yield();
        yield return CreateEntry("o0") with { SearchMode = SearchEntryMode.Include };
        await Task.Yield();
    }

    [Fact]
    public async Task GivenAMidEntryFailure_WhenStreamingABatchResponse_ThenTheBundleIsValidAndNoExceptionEscapes()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act — must NOT throw
        await StreamingBundleSerializer.SerializeStreamAsync(
            stream, "batch-response", EntriesWithCorruptResourceJsonResponseAsync());

        // Assert
        var root = JsonDocument.Parse(stream.ToArray()).RootElement;
        root.GetProperty("entry").EnumerateArray()
            .Any(e => e.GetProperty("response").GetProperty("status").GetString() == "500 Internal Server Error")
            .ShouldBeTrue();
    }

    private static BundleEntryResponse CreateEntryResponse(string resourceJson) => new()
    {
        StatusCode = 200,
        Status = "200 OK",
        ResourceJson = resourceJson,
    };

    private static async IAsyncEnumerable<BundleEntryResponse> EntriesWithCorruptResourceJsonResponseAsync()
    {
        yield return CreateEntryResponse("""{"resourceType":"Patient","id":"p0"}""");
        await Task.Yield();
        yield return CreateEntryResponse("{\"resourceType\":");
        await Task.Yield();
    }
}
