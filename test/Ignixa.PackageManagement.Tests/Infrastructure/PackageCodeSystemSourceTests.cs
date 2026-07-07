// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests.Infrastructure;

/// <summary>
/// Covers the CodeSystem code&#8594;display and membership resolution surface: nested concept
/// flattening, version-suffix tolerance, and the enumerable-vs-non-enumerable content distinction
/// that keeps a later membership check from over-rejecting.
/// </summary>
public sealed class PackageCodeSystemSourceTests
{
    private const string CompleteSystem = "http://example.org/widget-status";

    private const string CompleteCodeSystem = """
    {
      "resourceType":"CodeSystem","id":"widget-status",
      "url":"http://example.org/widget-status","content":"complete",
      "concept":[
        {"code":"active","display":"Active"},
        {"code":"retired","display":"Retired","concept":[{"code":"deep","display":"Deep"}]},
        {"code":"nodisplay"}
      ]
    }
    """;

    private const string FragmentCodeSystem = """
    {
      "resourceType":"CodeSystem","id":"partial",
      "url":"http://example.org/partial","content":"fragment",
      "concept":[{"code":"a","display":"A"}]
    }
    """;

    private static PackageCodeSystemSource BuildSource() => new(new[]
    {
        CodeSystem("widget-status", CompleteSystem, CompleteCodeSystem),
        CodeSystem("partial", "http://example.org/partial", FragmentCodeSystem),
    });

    [Fact]
    public void GivenCompleteCodeSystem_WhenGettingDisplay_ThenReturnsConceptDisplayIncludingNested()
    {
        var source = BuildSource();

        source.GetDisplay(CompleteSystem, "active").ShouldBe("Active");
        source.GetDisplay(CompleteSystem, "deep").ShouldBe("Deep");
    }

    [Fact]
    public void GivenConceptWithoutDisplay_WhenGettingDisplay_ThenReturnsNull()
    {
        var source = BuildSource();

        source.GetDisplay(CompleteSystem, "nodisplay").ShouldBeNull();
    }

    [Fact]
    public void GivenVersionedSystemUrl_WhenGettingDisplay_ThenVersionSuffixIsIgnored()
    {
        var source = BuildSource();

        source.GetDisplay($"{CompleteSystem}|2.0.0", "active").ShouldBe("Active");
    }

    [Fact]
    public void GivenUnknownSystem_WhenQuerying_ThenNotKnownAndMembershipUndecidable()
    {
        var source = BuildSource();

        source.IsKnownSystem("http://example.org/unknown").ShouldBeFalse();
        source.ContainsCode("http://example.org/unknown", "active").ShouldBeNull();
        source.GetDisplay("http://example.org/unknown", "active").ShouldBeNull();
    }

    [Fact]
    public void GivenCompleteSystem_WhenCodeMissing_ThenMembershipIsAuthoritativeFalse()
    {
        var source = BuildSource();

        source.IsKnownSystem(CompleteSystem).ShouldBeTrue();
        source.ContainsCode(CompleteSystem, "active").ShouldBe(true);
        source.ContainsCode(CompleteSystem, "missing").ShouldBe(false);
    }

    [Fact]
    public void GivenNonCompleteSystem_WhenCodeMissing_ThenMembershipIsUndecidable()
    {
        var source = BuildSource();

        // A fragment system does not enumerate its full code set: a hit is still true, but a miss
        // is unverifiable (null) so a downstream check degrades to a warning rather than rejecting.
        source.IsKnownSystem("http://example.org/partial").ShouldBeFalse();
        source.ContainsCode("http://example.org/partial", "a").ShouldBe(true);
        source.ContainsCode("http://example.org/partial", "missing").ShouldBeNull();
    }

    private static ExtractedResource CodeSystem(string id, string canonical, string json) => new()
    {
        ResourceType = "CodeSystem",
        Canonical = canonical,
        ResourceId = id,
        ResourceJson = json,
        FhirVersion = "4.0.1",
    };
}
