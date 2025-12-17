// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
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
        context.ShouldNotBeNull();
        context.ScenarioName.ShouldBe("Urinary Tract Infection");
        context.Patient.ShouldNotBeNull();
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesPatient()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(age: 35, gender: "female");

        // Assert
        context.Patient.ShouldNotBeNull();
        context.Patient!.MutableNode["gender"]?.GetValue<string>().ShouldBe("female");
    }

    #endregion

    #region Condition

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesUTICondition()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.Conditions.ShouldHaveSingleItem();
        var condition = context.Conditions.Single();
        condition.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>().ShouldBe("68566005");
        condition.MutableNode["code"]?["coding"]?[0]?["display"]?.GetValue<string>().ShouldBe("Urinary tract infectious disease");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIWithFollowUp_ThenConditionIsResolved()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(includeFollowUp: true);

        // Assert
        var condition = context.Conditions.Single();
        condition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>().ShouldBe("resolved");
        condition.MutableNode["abatementDateTime"].ShouldNotBeNull();
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIWithoutFollowUp_ThenConditionIsActive()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(includeFollowUp: false);

        // Assert
        var condition = context.Conditions.Single();
        condition.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>().ShouldBe("active");
        condition.MutableNode["abatementDateTime"].ShouldBeNull();
    }

    #endregion

    #region Encounters

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIWithFollowUp_ThenCreatesTwoEncounters()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(includeFollowUp: true);

        // Assert
        context.Encounters.Count.ShouldBe(2);
        context.Encounters[0].MutableNode["reasonCode"].ShouldNotBeNull();
        context.Encounters[1].MutableNode["reasonCode"].ShouldNotBeNull();
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIWithoutFollowUp_ThenCreatesOneEncounter()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection(includeFollowUp: false);

        // Assert
        context.Encounters.ShouldHaveSingleItem();
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

        temperatureObs.ShouldNotBeNull();
        var tempValue = temperatureObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        tempValue.ShouldBe(38.5m);
        temperatureObs.MutableNode["valueQuantity"]?["unit"]?.GetValue<string>().ShouldBe("Cel");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesPainSeverityObservation()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var painObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "72514-3");

        painObs.ShouldNotBeNull();
        var painValue = painObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        painValue.ShouldBe(5m);
    }

    #endregion

    #region Diagnostic Report - Urinalysis

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesUrinalysisDiagnosticReport()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.DiagnosticReports.ShouldHaveSingleItem();
        var report = context.DiagnosticReports.Single();
        report.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>().ShouldBe("24356-8");
        report.MutableNode["code"]?["coding"]?[0]?["display"]?.GetValue<string>().ShouldBe("Urinalysis complete panel - Urine");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenUrinalysisContainsLeukocyteEsterase()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var leukocyteObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "5799-2");

        leukocyteObs.ShouldNotBeNull();
        leukocyteObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>().ShouldBe(1m);
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenUrinalysisContainsNitrite()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var nitriteObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "5802-4");

        nitriteObs.ShouldNotBeNull();
        nitriteObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>().ShouldBe(1m);
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenUrinalysisContainsBacteria()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var bacteriaObs = context.Observations.FirstOrDefault(o =>
            o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>() == "25145-4");

        bacteriaObs.ShouldNotBeNull();
        bacteriaObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>().ShouldBe(1m);
    }

    #endregion

    #region Medication

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenCreatesNitrofurantoinMedication()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.Medications.ShouldHaveSingleItem();
        var medication = context.Medications.Single();
        medication.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>().ShouldBe("312017");
        medication.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["display"]?.GetValue<string>().ShouldContain("Nitrofurantoin");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenMedicationHasCorrectDosing()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var medication = context.Medications.Single();
        var dosage = medication.MutableNode["dosageInstruction"]?[0];
        dosage.ShouldNotBeNull();
        dosage!["timing"]?["repeat"]?["frequency"]?.GetValue<int>().ShouldBe(2); // Twice daily
        dosage["timing"]?["repeat"]?["period"]?.GetValue<int>().ShouldBe(1);
        dosage["timing"]?["repeat"]?["periodUnit"]?.GetValue<string>().ShouldBe("d");
    }

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenMedicationHasSevenDaySupply()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        var medication = context.Medications.Single();
        medication.MutableNode["status"]?.GetValue<string>().ShouldBe("active");
        medication.MutableNode["intent"]?.GetValue<string>().ShouldBe("order");
        var dispenseRequest = medication.MutableNode["dispenseRequest"];
        dispenseRequest?["quantity"]?["value"]?.GetValue<int>().ShouldBe(7);
    }

    #endregion

    #region Resource Relationships

    [Fact]
    public void GivenSchemaProvider_WhenGeneratingUTIScenario_ThenAllResourcesReferencePatient()
    {
        // Arrange & Act
        var context = _schemaProvider.GetUrinaryTractInfection();

        // Assert
        context.Patient.ShouldNotBeNull();
        var patientId = context.Patient!.Id;

        foreach (var condition in context.Conditions)
        {
            condition.MutableNode["subject"]?["reference"]?.GetValue<string>().ShouldBe($"urn:uuid:{patientId}");
        }

        foreach (var observation in context.Observations)
        {
            observation.MutableNode["subject"]?["reference"]?.GetValue<string>().ShouldBe($"urn:uuid:{patientId}");
        }

        foreach (var medication in context.Medications)
        {
            medication.MutableNode["subject"]?["reference"]?.GetValue<string>().ShouldBe($"urn:uuid:{patientId}");
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
        medication.MutableNode["reasonReference"]?[0]?["reference"]?.GetValue<string>().ShouldBe($"urn:uuid:{conditionId}");
    }

    #endregion
}
