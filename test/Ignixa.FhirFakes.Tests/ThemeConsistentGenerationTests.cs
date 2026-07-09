// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification.Generated;
using Shouldly;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Tests;

public class ThemeConsistentGenerationTests
{
    private static readonly Dictionary<string, FhirCode> ProcedurePool =
        BindingCodeMapper.GetAllProcedureCodes().ToDictionary(c => $"{c.System}|{c.Code}");

    private static readonly Dictionary<string, FhirCode> MedicationPool =
        BindingCodeMapper.GetAllMedicationCodes().ToDictionary(c => $"{c.System}|{c.Code}");

    [Fact]
    public void GivenMinimalDensityAndCardiologyTheme_WhenGeneratingMedicationRequest_ThenRequiredCodeIsThemed()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1)
        {
            Density = GenerationDensity.Minimal,
            Theme = ClinicalDomain.Cardiology,
        };

        AssertMedicationCodesAreCardiologyThemed(faker, requireMatch: true);
    }

    [Fact]
    public void GivenRealisticDensityAndCardiologyTheme_WhenGeneratingMedicationRequest_ThenRequiredCodeIsThemed()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1)
        {
            Density = GenerationDensity.Realistic,
            Theme = ClinicalDomain.Cardiology,
        };

        AssertMedicationCodesAreCardiologyThemed(faker, requireMatch: true);
    }

    [Fact]
    public void GivenMinimalDensityAndUnspecifiedTheme_WhenGeneratingMedicationRequest_ThenThemingIsDisabled()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1)
        {
            Density = GenerationDensity.Minimal,
            Theme = ClinicalDomain.Unspecified,
        };

        var resource = faker.Generate("MedicationRequest");

        ((IMutableJsonNode)resource).MutableNode["resourceType"]!.GetValue<string>().ShouldBe("MedicationRequest");
    }

    private static void AssertMedicationCodesAreCardiologyThemed(SchemaBasedFhirResourceFaker faker, bool requireMatch)
    {
        var matched = 0;
        for (var i = 0; i < 20; i++)
        {
            var resource = faker.Generate("MedicationRequest");
            foreach (var (system, code) in CollectCodings(((IMutableJsonNode)resource).MutableNode))
            {
                if (MedicationPool.TryGetValue($"{system}|{code}", out var medication))
                {
                    matched++;
                    (medication.Domain is ClinicalDomain.Cardiology or null).ShouldBeTrue(
                        $"Medication code {code} ({medication.Display}) has domain {medication.Domain}, expected Cardiology or untagged.");
                }
            }
        }

        if (requireMatch)
        {
            matched.ShouldBeGreaterThan(0,
                "Expected at least one themed medication code to be generated at this density to prove theming is exercised.");
        }
    }

    // Note: a Minimal-density + unset-Theme + MedicationRequest variant of this test was removed —
    // at Minimal density MedicationRequest has exactly one coded element (medicationCodeableConcept),
    // so a "domains observed in one call are consistent" assertion could never fail there and added
    // no regression protection. Minimal-density theming is already covered by
    // GivenMinimalDensityAndCardiologyTheme_WhenGeneratingMedicationRequest_ThenRequiredCodeIsThemed
    // (explicit theme reaches a required field); per-call consistency for the unset/default path is
    // covered below using Procedure, which has multiple coded siblings even at Minimal/Maximum.

    [Fact]
    public void GivenMaximumDensityAndUnsetTheme_WhenGeneratingProcedure_ThenEachResourceIsInternallyConsistent()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 77)
        {
            Density = GenerationDensity.Maximum,
            // Theme intentionally left unset — the auto-picked-per-call default path.
        };

        AssertPerCallDomainConsistency(faker, "Procedure", ProcedurePool);
    }

    private static void AssertPerCallDomainConsistency(
        SchemaBasedFhirResourceFaker faker, string resourceType, Dictionary<string, FhirCode> pool)
    {
        for (var i = 0; i < 30; i++)
        {
            var resource = faker.Generate(resourceType);

            var domainsThisCall = CollectCodings(((IMutableJsonNode)resource).MutableNode)
                .Select(c => pool.TryGetValue($"{c.System}|{c.Code}", out var match) ? match : null)
                .Where(match => match?.Domain is not null)
                .Select(match => match!.Domain!.Value)
                .Distinct()
                .ToList();

            domainsThisCall.Count.ShouldBeLessThanOrEqualTo(1,
                $"Within a single {resourceType} Generate() call the auto-picked theme should keep every " +
                $"themed pick coherent, but found multiple domains: {string.Join(", ", domainsThisCall)}.");
        }
    }

    [Fact]
    public void GivenCardiologyTheme_WhenGeneratingProcedure_ThenMatchedProcedureCodesAreCardiologyOrUntagged()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1234)
        {
            Density = GenerationDensity.Maximum,
            Theme = ClinicalDomain.Cardiology,
        };

        var resource = faker.Generate("Procedure");

        var codings = CollectCodings(((IMutableJsonNode)resource).MutableNode);
        foreach (var (system, code) in codings)
        {
            if (ProcedurePool.TryGetValue($"{system}|{code}", out var matched))
            {
                (matched.Domain is ClinicalDomain.Cardiology or null).ShouldBeTrue(
                    $"Procedure code {code} ({matched.Display}) has domain {matched.Domain}, expected Cardiology or untagged.");
            }
        }
    }

    [Fact]
    public void GivenUnspecifiedTheme_WhenGeneratingProcedure_ThenProducesValidResourceWithoutThrowing()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1234)
        {
            Density = GenerationDensity.Maximum,
            Theme = ClinicalDomain.Unspecified,
        };

        var resource = faker.Generate("Procedure");

        ((IMutableJsonNode)resource).MutableNode["resourceType"]!.GetValue<string>().ShouldBe("Procedure");
    }

    [Fact]
    public void GivenUnsetTheme_WhenGeneratingRepeatedly_ThenThemeIsResolvedPerCall()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 4321)
        {
            Density = GenerationDensity.Maximum,
        };

        var observedDomains = new HashSet<ClinicalDomain>();
        for (var i = 0; i < 30; i++)
        {
            var resource = faker.Generate("Procedure");
            foreach (var (system, code) in CollectCodings(((IMutableJsonNode)resource).MutableNode))
            {
                if (ProcedurePool.TryGetValue($"{system}|{code}", out var matched) && matched.Domain is { } domain)
                {
                    observedDomains.Add(domain);
                }
            }
        }

        // If _resolvedTheme were not reset per Generate call, every resource would draw the same
        // theme and only one procedure domain could ever appear.
        observedDomains.Count.ShouldBeGreaterThan(1);
    }

    private static List<(string System, string Code)> CollectCodings(JsonNode? node)
    {
        var results = new List<(string, string)>();
        Walk(node, results);
        return results;
    }

    private static void Walk(JsonNode? node, List<(string, string)> results)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["system"] is JsonValue systemValue && obj["code"] is JsonValue codeValue
                    && systemValue.TryGetValue<string>(out var system) && codeValue.TryGetValue<string>(out var code))
                {
                    results.Add((system, code));
                }

                foreach (var property in obj)
                {
                    Walk(property.Value, results);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Walk(item, results);
                }

                break;
        }
    }
}
