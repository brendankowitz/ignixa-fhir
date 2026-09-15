// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Shouldly;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Tests for StreamingBundleSerializer.SerializeHistoryAsync fullUrl construction.
/// Regression coverage for FHIR invariant bdl-8 ("fullUrl cannot be a version specific reference"),
/// which history bundles previously violated by suffixing fullUrl with "/_history/{versionId}".
/// </summary>
public class StreamingBundleSerializerHistoryTests
{
    [Fact]
    public async Task GivenHistoryEntryWithVersionId_WhenSerialized_ThenFullUrlOmitsHistorySegment()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "patient-1",
          "meta": { "versionId": "2" },
          "name": [{"family": "Test"}]
        }
        """;

        var entry = new SearchEntryResult(
            ResourceType: "Patient",
            ResourceId: "patient-1",
            VersionId: "2",
            LastModified: DateTimeOffset.UtcNow,
            ResourceBytes: Encoding.UTF8.GetBytes(resourceJson));

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream,
            "history",
            total: 1,
            entries: CreateAsyncEnumerable([entry]));

        // Assert
        var entryElement = ParseFirstEntry(outputStream);

        entryElement.GetProperty("fullUrl").GetString().ShouldBe("Patient/patient-1");
        entryElement.GetProperty("resource").GetProperty("meta").GetProperty("versionId").GetString().ShouldBe("2");
        entryElement.GetProperty("response").GetProperty("etag").GetString().ShouldBe("W/\"2\"");
    }

    [Fact]
    public async Task GivenHistoryEntryWithoutVersionId_WhenSerialized_ThenFullUrlIsTypeSlashId()
    {
        // Arrange
        var resourceJson = """
        {
          "resourceType": "Patient",
          "id": "patient-2",
          "name": [{"family": "Test"}]
        }
        """;

        var entry = new SearchEntryResult(
            ResourceType: "Patient",
            ResourceId: "patient-2",
            VersionId: string.Empty,
            LastModified: DateTimeOffset.UtcNow,
            ResourceBytes: Encoding.UTF8.GetBytes(resourceJson));

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream,
            "history",
            total: 1,
            entries: CreateAsyncEnumerable([entry]));

        // Assert
        var entryElement = ParseFirstEntry(outputStream);

        entryElement.GetProperty("fullUrl").GetString().ShouldBe("Patient/patient-2");
        entryElement.GetProperty("response").TryGetProperty("etag", out _).ShouldBeFalse();
    }

    private static async IAsyncEnumerable<SearchEntryResult> CreateAsyncEnumerable(List<SearchEntryResult> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            await Task.Yield();
        }
    }

    private static JsonElement ParseFirstEntry(MemoryStream outputStream)
    {
        outputStream.Position = 0;
        using var document = JsonDocument.Parse(outputStream);
        return document.RootElement.GetProperty("entry").EnumerateArray().First().Clone();
    }
}
