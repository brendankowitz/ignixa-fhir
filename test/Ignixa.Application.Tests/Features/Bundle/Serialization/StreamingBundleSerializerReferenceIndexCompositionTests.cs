// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle.Serialization;

/// <summary>
/// Guards that <see cref="StreamingBundleSerializer.SerializeHistoryAsync"/>'s version-agnostic
/// <c>fullUrl</c> (fixing FHIR invariant bdl-8) composes with <see cref="ReferenceIndex"/>'s
/// derived versioned keys: even though a serialized history entry's <c>fullUrl</c> no longer
/// carries <c>/_history/{versionId}</c>, an in-bundle versioned reference still resolves to the
/// specific version, because <see cref="ReferenceIndex"/> re-derives the versioned key from each
/// entry's own <c>resource.meta.versionId</c>. The two halves are unit-tested in isolation by
/// <c>StreamingBundleSerializerHistoryTests</c> (fullUrl construction) and
/// <c>ReferenceIndexTests</c> (index construction); this test pins that they still work together.
/// </summary>
public class StreamingBundleSerializerReferenceIndexCompositionTests
{
    private static readonly IFhirSchemaProvider R4Provider = FhirVersion.R4.GetSchemaProvider();

    [Fact]
    public async Task GivenSerializedHistoryBundleWithTwoVersions_WhenBuildingReferenceIndex_ThenVersionedReferencesResolveToDistinctVersions()
    {
        // Arrange - two versions of Patient/123, newest (versionId "2") first, each carrying
        // different content (gender) so a wrong-version resolution fails the assertion below
        // rather than merely returning a non-null element.
        var versionTwoJson = """
        {
          "resourceType": "Patient",
          "id": "123",
          "meta": { "versionId": "2" },
          "gender": "female"
        }
        """;
        var versionOneJson = """
        {
          "resourceType": "Patient",
          "id": "123",
          "meta": { "versionId": "1" },
          "gender": "male"
        }
        """;

        var entries = new List<SearchEntryResult>
        {
            new(
                ResourceType: "Patient",
                ResourceId: "123",
                VersionId: "2",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: Encoding.UTF8.GetBytes(versionTwoJson)),
            new(
                ResourceType: "Patient",
                ResourceId: "123",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow.AddMinutes(-1),
                ResourceBytes: Encoding.UTF8.GetBytes(versionOneJson)),
        };

        var outputStream = new MemoryStream();

        // Act
        await StreamingBundleSerializer.SerializeHistoryAsync(
            outputStream,
            "history",
            total: 2,
            entries: ToAsyncEnumerable(entries));

        outputStream.Position = 0;
        var bundleJson = Encoding.UTF8.GetString(outputStream.ToArray());
        var bundleElement = ResourceJsonNode.Parse(bundleJson).ToElement(R4Provider);
        var index = ReferenceIndex.Build(bundleElement);

        // Assert - each entry's fullUrl stays version-agnostic per bdl-8 ("fullUrl cannot be a
        // version specific reference"). Checked against the entry fullUrl values specifically
        // (not the whole payload) because a resource can legitimately carry a versioned reference
        // in its own content elsewhere in the bundle.
        var fullUrls = bundleElement.Children("entry")
            .SelectMany(entry => entry.Children("fullUrl"))
            .Select(fullUrl => fullUrl.Value?.ToString())
            .ToList();
        fullUrls.ShouldAllBe(fullUrl => fullUrl != null && !fullUrl.Contains("/_history/", StringComparison.Ordinal));

        // Assert - the ReferenceIndex's derived keys still resolve each version specifically,
        // in-bundle, with no ElementResolver configured.
        var versionTwoResolved = index.Resolve("Patient/123/_history/2");
        versionTwoResolved.ShouldNotBeNull();
        versionTwoResolved!.Children("gender").Single().Value.ShouldBe("female");

        var versionOneResolved = index.Resolve("Patient/123/_history/1");
        versionOneResolved.ShouldNotBeNull();
        versionOneResolved!.Children("gender").Single().Value.ShouldBe("male");

        // The version-agnostic reference resolves to the first (newest) entry, first-wins.
        var unversionedResolved = index.Resolve("Patient/123");
        unversionedResolved.ShouldNotBeNull();
        unversionedResolved!.Children("gender").Single().Value.ShouldBe("female");
    }

    private static async IAsyncEnumerable<SearchEntryResult> ToAsyncEnumerable(List<SearchEntryResult> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            await Task.Yield();
        }
    }
}
