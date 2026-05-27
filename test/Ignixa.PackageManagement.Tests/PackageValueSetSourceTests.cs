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
}
