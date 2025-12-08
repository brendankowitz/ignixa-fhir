// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for ImmunizationState. Tests vaccine records with dose tracking and series.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class ImmunizationStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenCreatesImmunization()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Vaccination visit")
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        scenario.Immunizations.Should().HaveCount(1);
        var immunization = scenario.Immunizations[0];
        immunization.ResourceType.Should().Be("Immunization");
        immunization.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenHasCorrectStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var status = immunization.MutableNode["status"]?.GetValue<string>();
        status.Should().Be("completed");
    }

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenHasCorrectVaccineCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var code = immunization.MutableNode["vaccineCode"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("140"); // CVX code for Influenza
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var patientRef = immunization.MutableNode["patient"]?["reference"]?.GetValue<string>();
        patientRef.Should().Be($"urn:uuid:{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenImmunization_WhenEncounterExists_ThenReferencesEncounter()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Vaccination visit")
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var encounterRef = immunization.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.Should().Be($"urn:uuid:{scenario.Encounters[0].Id}");
    }

    #endregion

    #region Dose Series Tests

    [Fact]
    public void GivenImmunizationWithDose_WhenGenerated_ThenHasDoseNumber()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImmunization(ImmunizationState.MMRDose1())
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var doseNumber = immunization.MutableNode["protocolApplied"]?[0]?["doseNumberPositiveInt"]?.GetValue<int>();
        doseNumber.Should().Be(1);
    }

    [Fact]
    public void GivenImmunizationSeries_WhenGenerated_ThenHasSeriesName()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImmunization(ImmunizationState.MMRDose1())
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var series = immunization.MutableNode["protocolApplied"]?[0]?["series"]?.GetValue<string>();
        series.Should().Be("Childhood Immunization Series");
    }

    [Fact]
    public void GivenImmunizationSeries_WhenGenerated_ThenHasSeriesDosesRecommended()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImmunization(ImmunizationState.MMRDose1())
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var dosesRecommended = immunization.MutableNode["protocolApplied"]?[0]?["seriesDosesPositiveInt"]?.GetValue<int>();
        dosesRecommended.Should().Be(2);
    }

    [Fact]
    public void GivenMultipleDoses_WhenGenerated_ThenTracksCorrectDoseNumbers()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImmunization(ImmunizationState.HepB(1))
            .DelayMonths(1)
            .AddImmunization(ImmunizationState.HepB(2))
            .DelayMonths(4)
            .AddImmunization(ImmunizationState.HepB(3))
            .Build();

        // Assert
        scenario.Immunizations.Should().HaveCount(3);

        var doses = scenario.Immunizations
            .Select(i => i.MutableNode["protocolApplied"]?[0]?["doseNumberPositiveInt"]?.GetValue<int>())
            .ToList();

        doses.Should().BeEquivalentTo([1, 2, 3]);
    }

    #endregion

    #region Route Validation Tests

    [Fact]
    public void GivenIntramuscularRoute_WhenGenerated_ThenHasCorrectRouteCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImmunization(Immunizations.Influenza, route: "IM")
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var routeCode = immunization.MutableNode["route"]?["coding"]?[0]?["code"]?.GetValue<string>();
        routeCode.Should().Be("IM");
    }

    [Fact]
    public void GivenSubcutaneousRoute_WhenGenerated_ThenHasCorrectRouteCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImmunization(Immunizations.MMR, route: "SC")
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var routeCode = immunization.MutableNode["route"]?["coding"]?[0]?["code"]?.GetValue<string>();
        routeCode.Should().Be("SC");
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenCovid19VaccineFactory_WhenGenerated_ThenHasCorrectDoseQuantity()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCovid19Vaccine(1)
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var quantity = immunization.MutableNode["doseQuantity"]?["value"]?.GetValue<decimal>();
        quantity.Should().Be(0.3m); // Pfizer specific dose
    }

    [Fact]
    public void GivenCovid19VaccineFactory_WhenGenerated_ThenHasManufacturer()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCovid19Vaccine(1)
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var manufacturer = immunization.MutableNode["manufacturer"]?["display"]?.GetValue<string>();
        manufacturer.Should().Be("Pfizer Inc.");
    }

    [Fact]
    public void GivenDTaPFactory_WhenGenerated_ThenHasCorrectSeriesDoses()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImmunization(ImmunizationState.DTaP(1))
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var dosesRecommended = immunization.MutableNode["protocolApplied"]?[0]?["seriesDosesPositiveInt"]?.GetValue<int>();
        dosesRecommended.Should().Be(5);
    }

    #endregion

    #region Metadata Tests

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenHasLotNumber()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var lotNumber = immunization.MutableNode["lotNumber"]?.GetValue<string>();
        lotNumber.Should().NotBeNullOrEmpty();
        lotNumber.Should().HaveLength(7); // Format: XX#####
    }

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenHasExpirationDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var expirationDate = immunization.MutableNode["expirationDate"]?.GetValue<string>();
        expirationDate.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenHasManufacturer()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var manufacturer = immunization.MutableNode["manufacturer"]?["display"]?.GetValue<string>();
        manufacturer.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenHasPerformer()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var performer = immunization.MutableNode["performer"]?[0]?["actor"]?["display"]?.GetValue<string>();
        performer.Should().NotBeNullOrEmpty();
        performer.Should().Contain("RN");
    }

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenHasSite()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var siteCode = immunization.MutableNode["site"]?["coding"]?[0]?["code"]?.GetValue<string>();
        siteCode.Should().BeOneOf("LA", "RA", "LT", "RT");
    }

    #endregion

    #region Timeline and Context Tests

    [Fact]
    public void GivenMultipleImmunizations_WhenGenerated_ThenAllAddedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddImmunization(ImmunizationState.HepB(1))
            .AddImmunization(ImmunizationState.DTaP(1))
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        scenario.Immunizations.Should().HaveCount(3);
    }

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunizationEvents = scenario.Timeline.Where(e => e.EventType == "Immunization").ToList();
        immunizationEvents.Should().HaveCount(1);
    }

    [Fact]
    public void GivenImmunization_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        scenario.AllResources.Should().Contain(scenario.Immunizations[0]);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenImmunizationWithoutEncounter_WhenGenerated_ThenCreatesWithoutEncounterReference()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var encounterRef = immunization.MutableNode["encounter"];
        encounterRef.Should().BeNull();
    }

    [Fact]
    public void GivenImmunizationWithPrimarySource_WhenGenerated_ThenPrimarySourceIsTrue()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddInfluenzaVaccine()
            .Build();

        // Assert
        var immunization = scenario.Immunizations[0];
        var primarySource = immunization.MutableNode["primarySource"]?.GetValue<bool>();
        primarySource.Should().BeTrue();
    }

    #endregion
}
