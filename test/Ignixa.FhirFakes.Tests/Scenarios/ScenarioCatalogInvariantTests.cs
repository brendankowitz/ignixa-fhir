// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Scenarios;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios;

public class ScenarioCatalogInvariantTests
{
    private static readonly string[] s_knownCategories =
        ["Chronic", "Emergency", "Pediatric", "Journey", "Oncology", "Acute", "Metabolic", "Preventive"];

    private static readonly string[] s_pinnedIds =
    [
        "AbdominalPainVisit",
        "AcuteMyocardialInfarction",
        "AdultAnnualPhysical",
        "AllergicMarchAsthma",
        "AsthmaticChild",
        "BMICorrelationDemo",
        "BreastCancerPathway",
        "COPDManagementWithExacerbations",
        "ChestPainVisit",
        "CholecystectomyPathway",
        "ChronicKidneyDiseaseProgression",
        "ColorectalCancerPathway",
        "ComprehensiveCancerScreening",
        "ComprehensiveScreeningVisit",
        "CongestiveHeartFailureExacerbation",
        "DepressionScreeningAndTreatment",
        "DiabeticPatient",
        "EnhancedWellnessVisit",
        "FractureVisit",
        "HypertensivePatient",
        "IschemicStrokeWithRehabilitation",
        "LungCancerPathway",
        "MetabolicSyndromeProgression",
        "MinorTraumaVisit",
        "PediatricAsthmaOnset",
        "PediatricEarInfection",
        "PediatricWellChildVisit",
        "PregnantPatient",
        "ProstateCancerPathway",
        "SeniorMedicareWellnessVisit",
        "SevereDepressionWithSuicidalIdeation",
        "TotalKneeReplacementPathway",
        "UrinaryTractInfection",
        "WellnessVisit",
    ];

    [Fact]
    public void GivenAllScenarios_WhenInspectingMetadata_ThenInvariantsHold()
    {
        var scenarios = ScenarioCatalog.GetAll();

        scenarios.ShouldNotBeEmpty();
        scenarios.Select(s => s.Id.ToUpperInvariant()).ShouldBeUnique();

        foreach (var scenario in scenarios)
        {
            scenario.Id.ShouldNotBeNullOrWhiteSpace();
            scenario.Title.ShouldNotBeNullOrWhiteSpace();
            if (scenario.Category is not null)
                s_knownCategories.ShouldContain(scenario.Category);

            if (scenario.Domain is { } domain)
                Enum.IsDefined(domain).ShouldBeTrue($"{scenario.Id}: undefined ClinicalDomain {domain}");

            foreach (var parameter in scenario.Parameters)
            {
                parameter.Name.ShouldNotBeNullOrWhiteSpace();
                IsSupportedParameterType(parameter.Type).ShouldBeTrue(
                    $"{scenario.Id}.{parameter.Name}: unsupported parameter type {parameter.Type.Name}");
                if (parameter is { Min: not null, Max: not null })
                    parameter.Min.Value.ShouldBeLessThanOrEqualTo(parameter.Max.Value);
                if (parameter is { HasDefaultValue: true, DefaultValue: not null, Min: not null, Max: not null }
                    && parameter.DefaultValue is int or decimal or double)
                {
                    var defaultAsDouble = Convert.ToDouble(parameter.DefaultValue, CultureInfo.InvariantCulture);
                    defaultAsDouble.ShouldBeInRange(parameter.Min.Value, parameter.Max.Value);
                }
            }
        }
    }

    [Fact]
    public void GivenTheCatalog_WhenListingIds_ThenMatchesThePinnedContract()
    {
        var ids = ScenarioCatalog.GetAll().Select(s => s.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        ids.ShouldBe(s_pinnedIds);
    }

    private static bool IsSupportedParameterType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(int) || underlying == typeof(decimal) || underlying == typeof(bool)
            || underlying == typeof(string) || underlying.IsEnum;
    }
}
