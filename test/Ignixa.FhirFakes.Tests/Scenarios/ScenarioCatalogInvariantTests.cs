// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Scenarios;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios;

[Collection(CatalogRegistrationGroup.Name)]
public class ScenarioCatalogInvariantTests
{
    private static readonly string[] KnownCategories =
        ["Chronic", "Emergency", "Pediatric", "Journey", "Oncology", "Acute", "Metabolic", "Preventive"];

    private static readonly string[] PinnedIds =
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
                KnownCategories.ShouldContain(scenario.Category);

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
        // Scoped to this library's own assembly: other tests may have registered additional
        // assemblies via ScenarioCatalog.RegisterAssembly, and that registration is process-lifetime
        // (no unregister), so the pinned contract must only cover this library's built-in scenarios.
        var ids = ScenarioCatalog.GetAll()
            .Where(s => s.Method.DeclaringType!.Assembly == typeof(ScenarioAttribute).Assembly)
            .Select(s => s.Id)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        ids.ShouldBe(PinnedIds);
    }

    [Fact]
    public void GivenEveryScenarioAttributedMethod_WhenScanningLoosely_ThenAllAppearInTheCatalog()
    {
        var catalogIds = ScenarioCatalog.GetAll().Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        var attributedMethods = typeof(ScenarioAttribute).Assembly.GetTypes()
            .Where(t => t.Namespace == "Ignixa.FhirFakes.Scenarios.Predefined")
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<ScenarioAttribute>() is not null)
            .ToList();

        attributedMethods.ShouldNotBeEmpty();

        foreach (var method in attributedMethods)
        {
            var attribute = method.GetCustomAttribute<ScenarioAttribute>();
            var id = attribute!.Id
                ?? (method.Name.StartsWith("Get", StringComparison.Ordinal) ? method.Name["Get".Length..] : method.Name);

            catalogIds.ShouldContain(id,
                $"[Scenario]-attributed method {method.DeclaringType!.Name}.{method.Name} derives id '{id}' " +
                "but it is absent from ScenarioCatalog.GetAll() — likely a malformed factory shape " +
                "(wrong first-parameter type or return type) silently dropped by discovery.");
        }
    }

    private static bool IsSupportedParameterType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(int) || underlying == typeof(decimal) || underlying == typeof(bool)
            || underlying == typeof(string) || underlying.IsEnum;
    }
}
