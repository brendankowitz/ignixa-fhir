// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios;

public class ScenarioCatalogTests
{
    [Fact]
    public void GivenScenarioCatalog_WhenGettingAll_ThenReturnsKnownScenarios()
    {
        var ids = ScenarioCatalog.All().Select(s => s.Id).ToList();

        ids.ShouldContain("DiabeticPatient");
        ids.ShouldContain("AsthmaticChild");
        ids.ShouldContain("PediatricEarInfection");
    }

    [Fact]
    public void GivenValidScenarioId_WhenFinding_ThenReturnsScenario()
    {
        var scenario = ScenarioCatalog.Find("DiabeticPatient");

        scenario.ShouldNotBeNull();
        scenario!.Id.ShouldBe("DiabeticPatient");
    }

    [Fact]
    public void GivenDifferentCasing_WhenFinding_ThenStillMatches()
    {
        var scenario = ScenarioCatalog.Find("diabeticpatient");

        scenario.ShouldNotBeNull();
    }

    [Fact]
    public void GivenUnknownScenarioId_WhenFinding_ThenReturnsNull()
    {
        var scenario = ScenarioCatalog.Find("NotAScenario");

        scenario.ShouldBeNull();
    }

    [Fact]
    public void GivenUnannotatedScenario_WhenFinding_ThenTitleFallsBackToHumanizedId()
    {
        // WellnessVisit is annotated later in this plan (Task 5); until then, any
        // as-yet-unannotated scenario id demonstrates the humanization fallback.
        // PediatricEarInfection has no consecutive-capital edge cases, so it humanizes cleanly.
        var scenario = ScenarioCatalog.Find("PediatricEarInfection")!;

        scenario.Title.ShouldBe("Pediatric Ear Infection");
        scenario.Category.ShouldBeNull();
    }

    [Fact]
    public void GivenValidScenario_WhenInvoking_ThenReturnsContextWithPatient()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = ScenarioCatalog.Find("DiabeticPatient")!;

        var context = ScenarioCatalog.Invoke(scenario, schemaProvider);

        context.Patient.ShouldNotBeNull();
        context.AllResources.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenParameterOverride_WhenInvoking_ThenOverriddenValueChangesGeneratedPatient()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = ScenarioCatalog.Find("DiabeticPatient")!;

        var defaultContext = ScenarioCatalog.Invoke(scenario, schemaProvider);
        var overriddenContext = ScenarioCatalog.Invoke(
            scenario, schemaProvider, new Dictionary<string, object?> { ["age"] = 85 });

        var defaultBirthYear = int.Parse(defaultContext.Patient!.MutableNode["birthDate"]!.ToString()![..4]);
        var overriddenBirthYear = int.Parse(overriddenContext.Patient!.MutableNode["birthDate"]!.ToString()![..4]);

        overriddenBirthYear.ShouldBeLessThan(defaultBirthYear);
    }

    [Fact]
    public void GivenParameterWithNoOverrideAndNoDefault_WhenInvoking_ThenFallsBackToTypeAppropriateDefault()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(ScenarioCatalogTests).GetMethod(
            nameof(RequiredParamScenario), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "RequiredParamScenario",
            Title = "RequiredParamScenario",
            Parameters = [],
            Method = method,
        };

        var context = ScenarioCatalog.Invoke(scenario, schemaProvider);

        context.GetAttribute<int>("requiredValue").ShouldBe(0);
    }

    [Fact]
    public void GivenScenarioMethodThatThrows_WhenInvoking_ThenWrapsInScenarioInvocationException()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(ScenarioCatalogTests).GetMethod(
            nameof(ThrowingScenario), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "ThrowingScenario",
            Title = "ThrowingScenario",
            Parameters = [],
            Method = method,
        };

        var exception = Should.Throw<ScenarioInvocationException>(
            () => ScenarioCatalog.Invoke(scenario, schemaProvider));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
        exception.InnerException!.Message.ShouldBe("boom");
    }

    private static ScenarioContext RequiredParamScenario(IFhirSchemaProvider schemaProvider, int requiredValue)
    {
        var context = new ScenarioContext();
        context.SetAttribute("requiredValue", requiredValue);
        return context;
    }

    private static ScenarioContext ThrowingScenario(IFhirSchemaProvider schemaProvider) =>
        throw new InvalidOperationException("boom");
}
