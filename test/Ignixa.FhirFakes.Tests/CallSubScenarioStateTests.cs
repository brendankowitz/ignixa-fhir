// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for CallSubScenarioState - scenario composition via reusable fragments.
/// </summary>
public class CallSubScenarioStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    [Fact]
    public void GivenSubScenario_WhenExecuted_ThenStatesAreAppliedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45, gender: "male")
            .AddEncounter("Annual wellness visit")
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Record Vitals")
            .Build();

        // Assert
        scenario.Observations.Should().HaveCountGreaterOrEqualTo(4, "should have at least height, weight, BMI, and blood pressure");

        // Verify specific vital signs were recorded
        var heightObs = scenario.Observations.FirstOrDefault(obs =>
            obs.MutableNode["code"]!["coding"]![0]!["code"]!.GetValue<string>() == "8302-2");
        heightObs.Should().NotBeNull("should have body height observation");

        var weightObs = scenario.Observations.FirstOrDefault(obs =>
            obs.MutableNode["code"]!["coding"]![0]!["code"]!.GetValue<string>() == "29463-7");
        weightObs.Should().NotBeNull("should have body weight observation");

        var bmiObs = scenario.Observations.FirstOrDefault(obs =>
            obs.MutableNode["code"]!["coding"]![0]!["code"]!.GetValue<string>() == "39156-5");
        bmiObs.Should().NotBeNull("should have BMI observation");
    }

    [Fact]
    public void GivenMultipleSubScenarios_WhenExecuted_ThenAllStatesAreApplied()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 50, gender: "female")
            .AddEncounter("Comprehensive health exam")
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Vitals")
            .AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Labs")
            .AddSubScenario(CommonScenarios.LipidPanel(), "Lipids")
            .Build();

        // Assert
        scenario.Observations.Should().HaveCountGreaterOrEqualTo(4, "should have vital signs");
        scenario.DiagnosticReports.Should().HaveCountGreaterOrEqualTo(2, "should have CMP and lipid panel");
    }

    [Fact]
    public void GivenCardiovascularVitals_WhenExecuted_ThenCorrectObservationsRecorded()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 65, gender: "male")
            .AddEncounter("Cardiac follow-up")
            .AddSubScenario(CommonScenarios.CardiovascularVitals(), "Cardiac Vitals")
            .Build();

        // Assert
        scenario.Observations.Should().HaveCountGreaterOrEqualTo(4, "should have heart rate, BP, and O2 sat");

        // Verify heart rate was recorded
        var heartRateObs = scenario.Observations.FirstOrDefault(obs =>
            obs.MutableNode["code"]!["coding"]![0]!["code"]!.GetValue<string>() == "8867-4");
        heartRateObs.Should().NotBeNull("should have heart rate observation");

        // Verify oxygen saturation was recorded
        var o2SatObs = scenario.Observations.FirstOrDefault(obs =>
            obs.MutableNode["code"]!["coding"]![0]!["code"]!.GetValue<string>() == "59408-5");
        o2SatObs.Should().NotBeNull("should have oxygen saturation observation");
    }

    [Fact]
    public void GivenCompleteBloodCount_WhenExecuted_ThenDiagnosticReportCreated()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 30, gender: "female")
            .AddEncounter("Infection workup")
            .AddSubScenario(CommonScenarios.CompleteBloodCount(), "CBC")
            .Build();

        // Assert
        scenario.DiagnosticReports.Should().HaveCount(1, "should have one CBC diagnostic report");
    }

    [Fact]
    public void GivenInfectionMonitoringVitals_WhenExecuted_ThenTemperatureAndRespiratoryRateRecorded()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 40, gender: "male")
            .AddEncounter("Suspected pneumonia")
            .AddSubScenario(CommonScenarios.InfectionMonitoringVitals(), "Infection Vitals")
            .Build();

        // Assert
        scenario.Observations.Should().HaveCountGreaterOrEqualTo(4, "should have temp, RR, HR, and O2 sat");

        // Verify temperature was recorded
        var tempObs = scenario.Observations.FirstOrDefault(obs =>
            obs.MutableNode["code"]!["coding"]![0]!["code"]!.GetValue<string>() == "8310-5");
        tempObs.Should().NotBeNull("should have body temperature observation");

        // Verify respiratory rate was recorded
        var rrObs = scenario.Observations.FirstOrDefault(obs =>
            obs.MutableNode["code"]!["coding"]![0]!["code"]!.GetValue<string>() == "9279-1");
        rrObs.Should().NotBeNull("should have respiratory rate observation");
    }

    [Fact]
    public void GivenSubScenarioWithDelay_WhenExecuted_ThenTimeAdvances()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddYears(-1);

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 35, gender: "female", startDate: startTime)
            .AddEncounter("Initial visit")
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Initial Vitals")
            .DelayMonths(3)
            .AddEncounter("Follow-up visit")
            .AddSubScenario(CommonScenarios.RecordVitalSigns(), "Follow-up Vitals")
            .Build();

        // Assert
        scenario.Encounters.Should().HaveCount(2, "should have initial and follow-up encounters");
        scenario.Observations.Should().HaveCountGreaterOrEqualTo(8, "should have vitals from both visits");

        // Verify time advanced
        var firstEncounterTime = DateTime.Parse(scenario.Encounters[0].MutableNode["period"]?["start"]?.GetValue<string>()!);
        var secondEncounterTime = DateTime.Parse(scenario.Encounters[1].MutableNode["period"]?["start"]?.GetValue<string>()!);

        (secondEncounterTime - firstEncounterTime).TotalDays.Should().BeGreaterThan(85, "should be approximately 3 months apart");
    }

    [Fact]
    public void GivenCustomSubScenario_WhenExecuted_ThenStatesApplied()
    {
        // Arrange
        Func<ScenarioBuilder, ScenarioBuilder> customSubScenario = builder => builder
            .AddObservation(VitalSigns.HeartRate, minValue: 100m, maxValue: 120m, unit: "/min", unitCode: "/min")
            .AddObservation(VitalSigns.BloodPressureSystolic, minValue: 140m, maxValue: 160m, unit: "mmHg", unitCode: "mm[Hg]");

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 55, gender: "male")
            .AddEncounter("Hypertension follow-up")
            .AddSubScenario(customSubScenario, "Elevated Vitals")
            .Build();

        // Assert
        scenario.Observations.Should().HaveCount(2, "should have heart rate and blood pressure");

        // Verify elevated values were used (heart rate should be >= 100)
        var heartRate = scenario.Observations.FirstOrDefault(obs =>
            obs.MutableNode["code"]!["coding"]![0]!["code"]!.GetValue<string>() == "8867-4");
        heartRate.Should().NotBeNull("should have heart rate observation");

        var hrValue = heartRate!.MutableNode["valueQuantity"]!["value"]!.GetValue<decimal>();
        hrValue.Should().BeGreaterOrEqualTo(100m, "heart rate should be elevated");
    }
}
