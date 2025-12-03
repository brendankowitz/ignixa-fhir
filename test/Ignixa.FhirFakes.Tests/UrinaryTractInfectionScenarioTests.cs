// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

public sealed class UrinaryTractInfectionScenarioTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Scenario Generation

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenReturnsValidContext()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.Should().NotBeNull();
        context.ScenarioName.Should().Be("Urinary Tract Infection");
        context.Patient.Should().NotBeNull();
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesPatient()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(age: 35, gender: "female");

        // Assert
        context.Patient.Should().NotBeNull();
        context.Patient!.MutableNode["gender"]?.GetValue<string>().Should().Be("female");
    }

    #endregion

    #region Condition

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesUTICondition()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.Conditions.Should().ContainSingle();
        var condition = context.Conditions.Single();
        condition.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>().Should().Be("68566005");
        condition.MutableNode["code"]?["coding"]?[0]?["display"]?.GetValue<string>().Should().Be("Urinary tract infectious disease");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIWithFollowUp_ThenConditionIsResolved()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(includeFollowUp: true);

        // Assert
        var condition = context.Conditions.Single();
        condition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>().Should().Be("resolved");
        condition.MutableNode["abatementDateTime"].Should().NotBeNull();
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIWithoutFollowUp_ThenConditionIsActive()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(includeFollowUp: false);

        // Assert
        var condition = context.Conditions.Single();
        condition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>().Should().Be("active");
        condition.MutableNode["abatementDateTime"].Should().BeNull();
    }

    #endregion

    #region Encounters

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIWithFollowUp_ThenCreatesTwoEncounters()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(includeFollowUp: true);

        // Assert
        context.Encounters.Should().HaveCount(2);
        context.Encounters[0].MutableNode["reasonCode"].Should().NotBeNull();
        context.Encounters[1].MutableNode["reasonCode"].Should().NotBeNull();
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIWithoutFollowUp_ThenCreatesOneEncounter()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(includeFollowUp: false);

        // Assert
        context.Encounters.Should().ContainSingle();
    }

    #endregion

    #region Observations

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesBodyTemperatureObservation()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var temperatureObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "8310-5");

        temperatureObs.Should().NotBeNull();
        var tempValue = temperatureObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        tempValue.Should().Be(38.5m);
        temperatureObs.MutableNode["valueQuantity"]?["unit"]?.GetValue<string>().Should().Be("Cel");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesPainSeverityObservation()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var painObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "72514-3");

        painObs.Should().NotBeNull();
        var painValue = painObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        painValue.Should().Be(5m);
    }

    #endregion

    #region Diagnostic Report - Urinalysis

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesUrinalysisDiagnosticReport()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.DiagnosticReports.Should().ContainSingle();
        var report = context.DiagnosticReports.Single();
        report.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>().Should().Be("24356-8");
        report.MutableNode["code"]?["coding"]?[0]?["display"]?.GetValue<string>().Should().Be("Urinalysis complete panel - Urine");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenUrinalysisContainsLeukocyteEsterase()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var leukocyteObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "5799-2");

        leukocyteObs.Should().NotBeNull();
        leukocyteObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>().Should().Be(1m);
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenUrinalysisContainsNitrite()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var nitriteObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "5802-4");

        nitriteObs.Should().NotBeNull();
        nitriteObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>().Should().Be(1m);
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenUrinalysisContainsBacteria()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var bacteriaObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "25145-4");

        bacteriaObs.Should().NotBeNull();
        bacteriaObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>().Should().Be(1m);
    }

    #endregion

    #region Medication

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesNitrofurantoinMedication()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.Medications.Should().ContainSingle();
        var medication = context.Medications.Single();
        medication.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>().Should().Be("312017");
        medication.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["display"]?.GetValue<string>().Should().Contain("Nitrofurantoin");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenMedicationHasCorrectDosing()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var medication = context.Medications.Single();
        var dosage = medication.MutableNode["dosageInstruction"]?[0];
        dosage.Should().NotBeNull();
        dosage!["timing"]?["repeat"]?["frequency"]?.GetValue<int>().Should().Be(2); // Twice daily
        dosage["timing"]?["repeat"]?["period"]?.GetValue<int>().Should().Be(1);
        dosage["timing"]?["repeat"]?["periodUnit"]?.GetValue<string>().Should().Be("d");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenMedicationHasSevenDaySupply()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var medication = context.Medications.Single();
        medication.MutableNode["status"]?.GetValue<string>().Should().Be("active");
        medication.MutableNode["intent"]?.GetValue<string>().Should().Be("order");
        var dispenseRequest = medication.MutableNode["dispenseRequest"];
        dispenseRequest?["quantity"]?["value"]?.GetValue<int>().Should().Be(7);
    }

    #endregion

    #region Resource Relationships

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenAllResourcesReferencePatient()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.Patient.Should().NotBeNull();
        var patientId = context.Patient!.Id;

        foreach (var condition in context.Conditions)
        {
            condition.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
        }

        foreach (var observation in context.Observations)
        {
            observation.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
        }

        foreach (var medication in context.Medications)
        {
            medication.MutableNode["subject"]?["reference"]?.GetValue<string>().Should().Be($"Patient/{patientId}");
        }
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenMedicationReferencesCondition()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var conditionId = context.Conditions.Single().Id;
        var medication = context.Medications.Single();
        medication.MutableNode["reasonReference"]?[0]?["reference"]?.GetValue<string>().Should().Be($"Condition/{conditionId}");
    }

    #endregion
}
