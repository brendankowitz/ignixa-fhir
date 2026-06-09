// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests;

/// <summary>
/// Tests for <see cref="PackageValueSetSource"/>, which exposes ValueSet + CodeSystem
/// resources extracted from a FHIR IG package as an <see cref="IValueSetProvider"/>.
/// </summary>
public class PackageValueSetSourceTests
{
    private static readonly string[] AlphaBeta = { "alpha", "beta" };
    private static readonly string[] AlphaBetaGamma = { "alpha", "beta", "gamma" };
    private static ExtractedResource MakeValueSet(string canonical, string id, string? version, string body)
        => new()
        {
            ResourceType = "ValueSet",
            Canonical = canonical,
            Version = version,
            ResourceId = id,
            ResourceJson = body,
            FhirVersion = "4.0.1",
        };

    private static ExtractedResource MakeCodeSystem(string canonical, string id, string? version, string body)
        => new()
        {
            ResourceType = "CodeSystem",
            Canonical = canonical,
            Version = version,
            ResourceId = id,
            ResourceJson = body,
            FhirVersion = "4.0.1",
        };

    private const string DemoValueSetWithInlineConcepts = """
        {
          "resourceType": "ValueSet",
          "id": "demo-vs",
          "url": "http://example.org/ValueSet/demo",
          "compose": {
            "include": [
              {
                "system": "http://example.org/CodeSystem/demo",
                "concept": [
                  { "code": "alpha", "display": "Alpha" },
                  { "code": "beta", "display": "Beta" }
                ]
              }
            ]
          }
        }
        """;

    private const string DemoValueSetReferencingCodeSystem = """
        {
          "resourceType": "ValueSet",
          "id": "demo-vs-ref",
          "url": "http://example.org/ValueSet/demo-ref",
          "compose": {
            "include": [ { "system": "http://example.org/CodeSystem/demo" } ]
          }
        }
        """;

    private const string DemoCodeSystem = """
        {
          "resourceType": "CodeSystem",
          "id": "demo-cs",
          "url": "http://example.org/CodeSystem/demo",
          "content": "complete",
          "concept": [
            { "code": "alpha", "display": "Alpha" },
            { "code": "beta", "display": "Beta" },
            { "code": "gamma", "display": "Gamma" }
          ]
        }
        """;

    [Fact]
    public void GivenValueSetWithInlineConcepts_WhenLookingUp_ThenReturnsCodes()
    {
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/demo", "demo-vs", version: null, DemoValueSetWithInlineConcepts),
        });

        var codes = source.GetCodes("http://example.org/ValueSet/demo");
        codes.ShouldNotBeNull();
        codes!.Select(c => c.Code).ShouldBe(AlphaBeta, ignoreOrder: true);
    }

    [Fact]
    public void GivenValueSetReferencingCodeSystem_WhenLookingUp_ThenExpandsViaCodeSystem()
    {
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/demo-ref", "demo-vs-ref", null, DemoValueSetReferencingCodeSystem),
            MakeCodeSystem("http://example.org/CodeSystem/demo", "demo-cs", null, DemoCodeSystem),
        });

        var codes = source.GetCodes("http://example.org/ValueSet/demo-ref");
        codes.ShouldNotBeNull();
        codes!.Select(c => c.Code).ShouldBe(AlphaBetaGamma, ignoreOrder: true);
    }

    [Fact]
    public void GivenUnknownValueSet_WhenLookingUp_ThenReturnsNull()
    {
        var source = new PackageValueSetSource(Array.Empty<ExtractedResource>());
        source.GetCodes("http://example.org/ValueSet/missing").ShouldBeNull();
        source.IsKnownValueSet("http://example.org/ValueSet/missing").ShouldBeFalse();
    }

    [Fact]
    public void GivenValueSetCanonicalWithVersionSuffix_WhenLookingUp_ThenStripsVersionForMatch()
    {
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/demo", "demo-vs", "1.0.0", DemoValueSetWithInlineConcepts),
        });

        source.GetCodes("http://example.org/ValueSet/demo|1.0.0").ShouldNotBeNull();
        source.IsKnownValueSet("http://example.org/ValueSet/demo|1.0.0").ShouldBeTrue();
    }

    [Fact]
    public void GivenKnownValueSetWithValidCode_WhenValidating_ThenReturnsTrue()
    {
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/demo", "demo-vs", null, DemoValueSetWithInlineConcepts),
        });

        source.IsValidCode("http://example.org/ValueSet/demo", "alpha").ShouldBe(true);
        source.IsValidCode("http://example.org/ValueSet/demo", "missing").ShouldBe(false);
        source.IsValidCode("http://example.org/ValueSet/unknown", "x").ShouldBeNull();
    }

    private const string IntensionalFilterValueSet = """
        {
          "resourceType": "ValueSet",
          "id": "filter-vs",
          "url": "http://example.org/ValueSet/filter",
          "compose": {
            "include": [
              {
                "system": "http://snomed.info/sct",
                "filter": [ { "property": "concept", "op": "is-a", "value": "123" } ]
              }
            ]
          }
        }
        """;

    private const string ValueSetChainValueSet = """
        {
          "resourceType": "ValueSet",
          "id": "chain-vs",
          "url": "http://example.org/ValueSet/chain",
          "compose": {
            "include": [ { "valueSet": [ "http://example.org/ValueSet/other" ] } ]
          }
        }
        """;

    private const string ValueSetWithExclude = """
        {
          "resourceType": "ValueSet",
          "id": "exclude-vs",
          "url": "http://example.org/ValueSet/exclude",
          "compose": {
            "include": [
              {
                "system": "http://example.org/CodeSystem/demo",
                "concept": [ { "code": "alpha" }, { "code": "beta" } ]
              }
            ],
            "exclude": [
              { "system": "http://example.org/CodeSystem/demo", "concept": [ { "code": "beta" } ] }
            ]
          }
        }
        """;

    [Fact]
    public void GivenIntensionalFilterValueSet_WhenExpanding_ThenReturnsNullSoBindingDegradesToWarning()
    {
        // A filter-based (intensional) include cannot be enumerated. Returning null (rather than
        // an empty set) lets a required binding degrade to a warning instead of rejecting every
        // otherwise-valid code.
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/filter", "filter-vs", null, IntensionalFilterValueSet),
        });

        source.GetCodes("http://example.org/ValueSet/filter").ShouldBeNull();
        source.IsValidCode("http://example.org/ValueSet/filter", "anything").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetChainInclude_WhenExpanding_ThenReturnsNull()
    {
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/chain", "chain-vs", null, ValueSetChainValueSet),
        });

        source.GetCodes("http://example.org/ValueSet/chain").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetReferencingMissingCodeSystem_WhenExpanding_ThenReturnsNull()
    {
        // CodeSystem is not supplied alongside the ValueSet, so the whole-CodeSystem inclusion
        // cannot be enumerated.
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/demo-ref", "demo-vs-ref", null, DemoValueSetReferencingCodeSystem),
        });

        source.GetCodes("http://example.org/ValueSet/demo-ref").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetWithExclude_WhenExpanding_ThenReturnsNull()
    {
        // compose.exclude makes an include-only expansion an over-approximation; membership
        // cannot be soundly decided, so the ValueSet is reported as unexpandable.
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/exclude", "exclude-vs", null, ValueSetWithExclude),
            MakeCodeSystem("http://example.org/CodeSystem/demo", "demo-cs", null, DemoCodeSystem),
        });

        source.GetCodes("http://example.org/ValueSet/exclude").ShouldBeNull();
    }

    // ===== New tests required by PR review =====

    [Fact]
    public void GivenMalformedValueSetJson_WhenExpanding_ThenReturnsNullAndDoesNotThrow()
    {
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/broken", "broken-vs", null, "{ this is not valid json"),
        });

        var result = source.GetCodes("http://example.org/ValueSet/broken");
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenVersionedCanonicalKey_WhenLookingUpWithBareUrl_ThenFindsValueSet()
    {
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/demo|1.0.0", "demo-vs", "1.0.0", DemoValueSetWithInlineConcepts),
        });

        var codes = source.GetCodes("http://example.org/ValueSet/demo");
        codes.ShouldNotBeNull();
        codes!.Select(c => c.Code).ShouldBe(AlphaBeta, ignoreOrder: true);
    }

    [Fact]
    public void GivenUnexpandableValueSet_WhenQueriedMultipleTimes_ThenReturnsCachedNull()
    {
        var source = new PackageValueSetSource(new[]
        {
            MakeValueSet("http://example.org/ValueSet/filter", "filter-vs", null, IntensionalFilterValueSet),
        });

        source.GetCodes("http://example.org/ValueSet/filter").ShouldBeNull();
        source.GetCodes("http://example.org/ValueSet/filter").ShouldBeNull();
        source.GetCodes("http://example.org/ValueSet/filter").ShouldBeNull();
    }
}
