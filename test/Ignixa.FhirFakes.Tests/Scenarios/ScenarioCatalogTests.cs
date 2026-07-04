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
        var ids = ScenarioCatalog.GetAll().Select(s => s.Id).ToList();

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
        // GetComprehensiveScreeningVisit is NOT annotated (outside the scope of the 14 screenshot-mapped scenarios).
        // It will fall back to humanized id for Title and null for Category.
        var scenario = ScenarioCatalog.Find("ComprehensiveScreeningVisit")!;

        scenario.Title.ShouldBe("Comprehensive Screening Visit");
        scenario.Category.ShouldBeNull();
    }

    [Fact]
    public void GivenAnnotatedScenario_WhenFindingDiabeticPatient_ThenHasExpectedMetadata()
    {
        var scenario = ScenarioCatalog.Find("DiabeticPatient")!;

        scenario.Category.ShouldBe("Chronic");
        scenario.Title.ShouldBe("Type 2 Diabetes");
        var age = scenario.Parameters.Single(p => p.Name == "age");
        age.Min.ShouldBe(18);
        age.Max.ShouldBe(90);
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

    [Fact]
    public void GivenParameterOverrideWithDifferentCasing_WhenInvoking_ThenAppliesOverride()
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

        var context = ScenarioCatalog.Invoke(
            scenario, schemaProvider, new Dictionary<string, object?> { ["REQUIREDVALUE"] = 42 });

        context.GetAttribute<int>("requiredValue").ShouldBe(42);
    }

    [Fact]
    public void GivenEnumParameterWithNoOverrideAndNoDefault_WhenInvoking_ThenFallsBackToEnumDefault()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(ScenarioCatalogTests).GetMethod(
            nameof(EnumParamScenario), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "EnumParamScenario",
            Title = "EnumParamScenario",
            Parameters = [],
            Method = method,
        };

        var context = ScenarioCatalog.Invoke(scenario, schemaProvider);

        context.GetAttribute<Severity>("severity").ShouldBe(Severity.Low);
    }

    [Fact]
    public void GivenAnnotatedScenario_WhenInvoking_ThenStampsClinicalDomain()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = ScenarioCatalog.Find("DiabeticPatient")!;

        var context = ScenarioCatalog.Invoke(scenario, schemaProvider);

        context.GetAttribute<ClinicalDomain>(ScenarioCatalog.ClinicalDomainAttributeKey)
            .ShouldBe(ClinicalDomain.Endocrinology);
    }

    [Fact]
    public void GivenUnannotatedScenario_WhenInvoking_ThenDoesNotStampClinicalDomain()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = ScenarioCatalog.Find("ComprehensiveScreeningVisit")!;

        var context = ScenarioCatalog.Invoke(scenario, schemaProvider);

        context.HasAttribute(ScenarioCatalog.ClinicalDomainAttributeKey).ShouldBeFalse();
    }

    [Fact]
    public void GivenLongOverrideForIntParameter_WhenInvoking_ThenCoercesToInt()
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

        // A compatible numeric type (long here) for an int parameter is a common shape when values
        // arrive from JSON deserialization or another loosely-typed caller -- should coerce, not throw.
        var context = ScenarioCatalog.Invoke(scenario, schemaProvider, new Dictionary<string, object?> { ["requiredValue"] = 42L });

        context.GetAttribute<int>("requiredValue").ShouldBe(42);
    }

    [Fact]
    public void GivenStringOverrideForIntParameter_WhenInvoking_ThenThrowsArgumentException()
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

        var exception = Should.Throw<ArgumentException>(
            () => ScenarioCatalog.Invoke(scenario, schemaProvider, new Dictionary<string, object?> { ["requiredValue"] = "notanint" }));

        exception.Message.ShouldContain("RequiredParamScenario");
        exception.Message.ShouldContain("requiredValue");
    }

    [Fact]
    public void GivenNullOverrideForNonNullableIntParameter_WhenInvoking_ThenThrowsArgumentException()
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

        var exception = Should.Throw<ArgumentException>(
            () => ScenarioCatalog.Invoke(scenario, schemaProvider, new Dictionary<string, object?> { ["requiredValue"] = null }));

        exception.Message.ShouldContain("RequiredParamScenario");
        exception.Message.ShouldContain("requiredValue");
    }

    private static ScenarioContext RequiredParamScenario(IFhirSchemaProvider schemaProvider, int requiredValue)
    {
        var context = new ScenarioContext();
        context.SetAttribute("requiredValue", requiredValue);
        return context;
    }

    private static ScenarioContext EnumParamScenario(IFhirSchemaProvider schemaProvider, Severity severity)
    {
        var context = new ScenarioContext();
        context.SetAttribute("severity", severity);
        return context;
    }

    private enum Severity
    {
        Low = 0,
        High = 1,
    }

    private static ScenarioContext ThrowingScenario(IFhirSchemaProvider schemaProvider) =>
        throw new InvalidOperationException("boom");
}
