// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for WellnessVisitScenario. Tests annual wellness visit generation with vital signs and lab panels.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class WellnessVisitScenarioTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenCreatesAllExpectedResources()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
        scenario.DiagnosticReports.Count.ShouldBe(2); // BMP + Lipid Panel
        scenario.Observations.Count.ShouldBeGreaterThanOrEqualTo(7); // 7 vital signs + lab observations
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenCreatesPatient()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");
        scenario.Patient.Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenCreatesWellnessEncounter()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        scenario.Encounters.Count.ShouldBe(1);
        var encounter = scenario.Encounters[0];
        encounter.ResourceType.ShouldBe("Encounter");
        encounter.Id.ShouldNotBeNullOrEmpty();
    }

    #endregion

    #region Vital Signs Tests

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasSevenVitalSigns()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert - Filter observations to vital signs only
        var vitalSigns = scenario.Observations
            .Where(o =>
            {
                var category = o.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
                return category == "vital-signs";
            })
            .ToList();

        vitalSigns.Count.ShouldBe(7); // Height, Weight, BMI, BP Systolic, BP Diastolic, Heart Rate, Respiratory Rate, Temperature
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasBodyHeightObservation()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var heightObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "8302-2"; // LOINC code for body height
        });

        heightObs.ShouldNotBeNull();
        var value = heightObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value!.Value.ShouldBeGreaterThan(0);
        var unit = heightObs.MutableNode["valueQuantity"]?["unit"]?.GetValue<string>();
        unit.ShouldBe("cm");
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasBodyWeightObservation()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var weightObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "29463-7"; // LOINC code for body weight
        });

        weightObs.ShouldNotBeNull();
        var value = weightObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value!.Value.ShouldBeGreaterThan(0);
        var unit = weightObs.MutableNode["valueQuantity"]?["unit"]?.GetValue<string>();
        unit.ShouldBe("kg");
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasBMIObservation()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var bmiObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "39156-5"; // LOINC code for BMI
        });

        bmiObs.ShouldNotBeNull();
        var value = bmiObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value!.Value.ShouldBeGreaterThan(0);
        var unit = bmiObs.MutableNode["valueQuantity"]?["unit"]?.GetValue<string>();
        unit.ShouldBe("kg/m2");
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasBloodPressureObservation()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var bpObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "85354-9"; // LOINC code for blood pressure panel
        });

        bpObs.ShouldNotBeNull();
        var components = bpObs!.MutableNode["component"] as System.Text.Json.Nodes.JsonArray;
        components.ShouldNotBeNull();
        components!.Count.ShouldBe(2); // Systolic and diastolic
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasHeartRateObservation()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var hrObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "8867-4"; // LOINC code for heart rate
        });

        hrObs.ShouldNotBeNull();
        var value = hrObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value!.Value.ShouldBeGreaterThan(0);
        var unit = hrObs.MutableNode["valueQuantity"]?["unit"]?.GetValue<string>();
        unit.ShouldBe("beats/minute");
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasRespiratoryRateObservation()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var rrObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "9279-1"; // LOINC code for respiratory rate
        });

        rrObs.ShouldNotBeNull();
        var value = rrObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value!.Value.ShouldBeGreaterThan(0);
        var unit = rrObs.MutableNode["valueQuantity"]?["unit"]?.GetValue<string>();
        unit.ShouldBe("breaths/minute");
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasBodyTemperatureObservation()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var tempObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "8310-5"; // LOINC code for body temperature
        });

        tempObs.ShouldNotBeNull();
        var value = tempObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value!.Value.ShouldBeGreaterThan(0);
        var unit = tempObs.MutableNode["valueQuantity"]?["unit"]?.GetValue<string>();
        unit.ShouldBe("Cel");
    }

    #endregion

    #region Basic Metabolic Panel Tests

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasBasicMetabolicPanel()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var bmpReport = scenario.DiagnosticReports.FirstOrDefault(dr =>
        {
            var code = dr.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "51990-0"; // LOINC code for BMP
        });

        bmpReport.ShouldNotBeNull();
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenBMPHasEightObservations()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var bmpReport = scenario.DiagnosticReports.FirstOrDefault(dr =>
        {
            var code = dr.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "51990-0"; // LOINC code for BMP
        });

        bmpReport.ShouldNotBeNull();
        var results = bmpReport!.MutableNode["result"] as System.Text.Json.Nodes.JsonArray;
        results.ShouldNotBeNull();
        results!.Count.ShouldBe(8); // Glucose, BUN, Creatinine, Sodium, Potassium, Chloride, CO2, Calcium
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenBMPHasGlucoseTest()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert - Find glucose observation in lab observations
        var glucoseObs = scenario.Observations.FirstOrDefault(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            var category = o.MutableNode["category"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "2339-0" && category == "laboratory"; // LOINC code for glucose
        });

        glucoseObs.ShouldNotBeNull();
        var value = glucoseObs!.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value!.Value.ShouldBeGreaterThan(0);
    }

    #endregion

    #region Lipid Panel Tests

    [Fact]
    public void GivenWellnessVisitWithDefaultAge_WhenGenerated_ThenIncludesLipidPanel()
    {
        // Arrange & Act - Default age is 45, which should include lipid panel
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var lipidReport = scenario.DiagnosticReports.FirstOrDefault(dr =>
        {
            var code = dr.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "24331-1"; // LOINC code for Lipid Panel
        });

        lipidReport.ShouldNotBeNull();
    }

    [Fact]
    public void GivenWellnessVisitAge30OrAbove_WhenGenerated_ThenIncludesLipidPanel()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit(age: 30, includeLipidPanel: false);

        // Assert - Lipid panel should be included automatically for age >= 30
        var lipidReport = scenario.DiagnosticReports.FirstOrDefault(dr =>
        {
            var code = dr.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "24331-1"; // LOINC code for Lipid Panel
        });

        lipidReport.ShouldNotBeNull();
    }

    [Fact]
    public void GivenWellnessVisitUnder30WithoutLipidPanel_WhenGenerated_ThenDoesNotIncludeLipidPanel()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit(age: 25, includeLipidPanel: false);

        // Assert - Should only have BMP, not lipid panel
        scenario.DiagnosticReports.Count.ShouldBe(1);
        var bmpReport = scenario.DiagnosticReports.FirstOrDefault(dr =>
        {
            var code = dr.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "51990-0"; // LOINC code for BMP
        });

        bmpReport.ShouldNotBeNull();
    }

    [Fact]
    public void GivenWellnessVisitUnder30WithLipidPanel_WhenGenerated_ThenIncludesLipidPanel()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit(age: 25, includeLipidPanel: true);

        // Assert - Should have both BMP and Lipid Panel
        scenario.DiagnosticReports.Count.ShouldBe(2);
        var lipidReport = scenario.DiagnosticReports.FirstOrDefault(dr =>
        {
            var code = dr.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "24331-1"; // LOINC code for Lipid Panel
        });

        lipidReport.ShouldNotBeNull();
    }

    [Fact]
    public void GivenWellnessVisitWithLipidPanel_WhenGenerated_ThenHasFourObservations()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        var lipidReport = scenario.DiagnosticReports.FirstOrDefault(dr =>
        {
            var code = dr.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "24331-1"; // LOINC code for Lipid Panel
        });

        lipidReport.ShouldNotBeNull();
        var results = lipidReport!.MutableNode["result"] as System.Text.Json.Nodes.JsonArray;
        results.ShouldNotBeNull();
        results!.Count.ShouldBe(4); // Total Cholesterol, HDL, LDL, Triglycerides
    }

    #endregion

    #region Parameter Tests

    [Fact]
    public void GivenWellnessVisitWithCustomAge_WhenGenerated_ThenPatientHasCorrectAge()
    {
        // Arrange
        var customAge = 60;

        // Act
        var scenario = _schemaProvider.GetWellnessVisit(age: customAge);

        // Assert
        scenario.CurrentAge.ShouldBe(customAge);
    }

    [Fact]
    public void GivenWellnessVisitWithCustomGender_WhenGenerated_ThenPatientHasCorrectGender()
    {
        // Arrange
        var customGender = "female";

        // Act
        var scenario = _schemaProvider.GetWellnessVisit(gender: customGender);

        // Assert
        var gender = scenario.Patient!.MutableNode["gender"]?.GetValue<string>();
        gender.ShouldBe(customGender);
    }

    #endregion

    #region Timeline Tests

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasTimelineEvents()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        scenario.Timeline.ShouldNotBeEmpty();
        scenario.Timeline.ShouldContain(e => e.EventType == "Encounter");
        scenario.Timeline.ShouldContain(e => e.EventType == "Observation");
        scenario.Timeline.ShouldContain(e => e.EventType == "DiagnosticReport");
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenAllResourcesInAllResourcesList()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        scenario.AllResources.ShouldNotBeEmpty();
        scenario.AllResources.ShouldContain(scenario.Encounters[0]);
        scenario.AllResources.ShouldContain(scenario.DiagnosticReports[0]);
    }

    #endregion

    #region Resource Count Tests

    [Fact]
    public void GivenWellnessVisitWithLipidPanel_WhenGenerated_ThenHasMinimum20Resources()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert - 1 Patient + 1 Encounter + 7 vitals + 8 BMP obs + 4 lipid obs + 2 diagnostic reports = 23 total
        scenario.AllResources.Count.ShouldBeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void GivenWellnessVisitWithoutLipidPanel_WhenGenerated_ThenHasMinimum17Resources()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit(age: 25, includeLipidPanel: false);

        // Assert - 1 Patient + 1 Encounter + 7 vitals + 8 BMP obs + 1 diagnostic report = 18 total
        scenario.AllResources.Count.ShouldBeGreaterThanOrEqualTo(17);
    }

    #endregion

    #region Scenario Name and Description Tests

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasCorrectScenarioName()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        scenario.ScenarioName.ShouldBe("Annual Wellness Visit");
    }

    [Fact]
    public void GivenWellnessVisit_WhenGenerated_ThenHasDescription()
    {
        // Arrange & Act
        var scenario = _schemaProvider.GetWellnessVisit();

        // Assert
        scenario.Description.ShouldNotBeNullOrEmpty();
        scenario.Description.ShouldContain("wellness");
    }

    #endregion
}
