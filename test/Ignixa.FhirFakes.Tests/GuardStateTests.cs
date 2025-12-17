// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for GuardState - conditional gates in scenario execution.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class GuardStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Age-Based Guard Tests

    [Fact]
    public void GivenPatientUnder18_WhenGuardRequiresMinimumAge18_ThenThrowsException()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 15)
            .AddGuard(GuardState.MinimumAge(18));

        // Act & Assert
        var act = () => scenario.Build();
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("Guard condition not met");
    }

    [Fact]
    public void GivenPatientOver18_WhenGuardRequiresMinimumAge18_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 25)
            .AddGuard(GuardState.MinimumAge(18))
            .AddEncounter("Adult visit")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenPatientOver65_WhenGuardRequiresMaximumAge65_ThenThrowsException()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 70)
            .AddGuard(GuardState.MaximumAge(65));

        // Act & Assert
        var act = () => scenario.Build();
        Should.Throw<InvalidOperationException>(act);
    }

    [Fact]
    public void GivenPatient45_WhenGuardRequiresAgeRange35To50_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 45)
            .AddGuard(GuardState.AgeRange(35, 45))
            .AddEncounter("Visit")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenPatient50_WhenGuardRequiresExactAge50_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 50)
            .AddGuard(GuardState.ExactAge(50))
            .AddEncounter("Visit")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    #endregion

    #region Attribute-Based Guard Tests

    [Fact]
    public void GivenAttributeNotSet_WhenGuardRequiresAttribute_ThenThrowsException()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddGuard(GuardState.RequireAttribute("diabetes_diagnosed"));

        // Act & Assert
        var act = () => scenario.Build();
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("Guard condition not met");
    }

    [Fact]
    public void GivenAttributeSet_WhenGuardRequiresAttribute_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .SetAttribute("diabetes_diagnosed", true)
            .AddGuard(GuardState.RequireAttribute("diabetes_diagnosed"))
            .AddEncounter("Follow-up visit")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAttributeValue3_WhenGuardRequiresEquals3_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .SetAttribute("severity", 3)
            .AddGuard(GuardState.AttributeEquals("severity", 3))
            .AddEncounter("High severity treatment")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAttributeValue2_WhenGuardRequiresEquals3_ThenThrowsException()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .SetAttribute("severity", 2)
            .AddGuard(GuardState.AttributeEquals("severity", 3));

        // Act & Assert
        var act = () => scenario.Build();
        Should.Throw<InvalidOperationException>(act);
    }

    [Fact]
    public void GivenAttributeValue5_WhenGuardRequiresGreaterThanOrEqual3_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .SetAttribute("disease_stage", 5)
            .AddGuard(GuardState.AttributeGreaterThanOrEqual("disease_stage", 3))
            .AddEncounter("Advanced stage treatment")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAttributeValue2_WhenGuardRequiresLessThanOrEqual3_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .SetAttribute("risk_score", 2)
            .AddGuard(GuardState.AttributeLessThanOrEqual("risk_score", 3))
            .AddEncounter("Low risk protocol")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAttributeValue3_WhenGuardRequiresNotEquals5_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .SetAttribute("status", 3)
            .AddGuard(GuardState.AttributeNotEquals("status", 5))
            .AddEncounter("Visit")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    #endregion

    #region Age-Appropriate Immunization Scenario

    [Fact]
    public void GivenChildUnder13_WhenGuardRequiresAge13_ThenHPVVaccineIsNotGiven()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 11)
            .AddWellnessVisit("Annual checkup")
            .AddGuard(GuardState.MinimumAge(13)) // HPV vaccine recommended at age 11-12, but using 13 for test
            .AddImmunization(Immunizations.HPV);

        // Act & Assert
        var act = () => scenario.Build();
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("Guard condition not met");
    }

    [Fact]
    public void GivenTeenager13_WhenGuardRequiresAge13_ThenHPVVaccineIsGiven()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 13)
            .AddWellnessVisit("Annual checkup")
            .AddGuard(GuardState.MinimumAge(13))
            .AddImmunization(Immunizations.HPV)
            .Build();

        // Assert
        scenario.Immunizations.Count.ShouldBe(1);
        var hpv = scenario.Immunizations[0];
        var code = hpv.MutableNode["vaccineCode"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.ShouldBe(Immunizations.HPV.Code);
    }

    #endregion

    #region Sequential Care Pathway Scenario

    [Fact]
    public void GivenDiabetesProgression_WhenGuardsCheckSeverity_ThenEscalatesCorrectly()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Diabetes Progression with Guards")
            .WithPatient(age: 50)
            .AddConditionOnset(FhirCode.Conditions.DiabetesType2, severity: 1, assignToAttribute: "diabetes_id")
            .SetAttribute("severity", 1)
            .AddEncounter("Initial diagnosis")
            .AddMedicationOrder(FhirCode.Medications.Metformin500mg)
            .DelayMonths(6)
            // Severity increases
            .IncrementAttribute("severity", 2)
            .AddEncounter("6-month follow-up")
            .AddGuard(GuardState.AttributeGreaterThanOrEqual("severity", 2))
            .AddMedicationOrder(FhirCode.Medications.InsulinGlargine)
            .DelayMonths(6)
            .AddEncounter("12-month follow-up")
            .Build();

        // Assert
        scenario.Encounters.Count.ShouldBe(3);
        scenario.Medications.Count.ShouldBe(2);
        scenario.GetAttribute<int>("severity").ShouldBe(3);
    }

    #endregion

    #region Condition-Dependent Procedure Scenario

    [Fact]
    public void GivenColonoscopy_WhenGuardRequiresAge45_ThenProcedureOnlyAfter45()
    {
        // Arrange
        var scenarioUnder45 = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 40)
            .AddWellnessVisit("Annual physical")
            .AddGuard(GuardState.MinimumAge(45))
            .AddColonoscopy("Normal findings");

        var scenarioOver45 = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 50)
            .AddWellnessVisit("Annual physical")
            .AddGuard(GuardState.MinimumAge(45))
            .AddColonoscopy("Normal findings");

        // Act & Assert
        var actUnder45 = () => scenarioUnder45.Build();
        Should.Throw<InvalidOperationException>(actUnder45).Message.ShouldContain("Guard condition not met");

        var contextOver45 = scenarioOver45.Build();
        contextOver45.Procedures.Count.ShouldBe(1);
    }

    #endregion

    #region Multiple Guard Combinations

    [Fact]
    public void GivenMultipleGuards_WhenAllConditionsMet_ThenSucceeds()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 55)
            .SetAttribute("diabetes_diagnosed", true)
            .SetAttribute("severity", 3)
            .AddGuard(GuardState.MinimumAge(50))
            .AddGuard(GuardState.MaximumAge(65))
            .AddGuard(GuardState.RequireAttribute("diabetes_diagnosed"))
            .AddGuard(GuardState.AttributeGreaterThanOrEqual("severity", 3))
            .AddEncounter("Complex care visit")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenMultipleGuards_WhenOneConditionFails_ThenThrowsException()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 70) // Over 65
            .SetAttribute("diabetes_diagnosed", true)
            .SetAttribute("severity", 3)
            .AddGuard(GuardState.MinimumAge(50))
            .AddGuard(GuardState.MaximumAge(65)) // This will fail
            .AddGuard(GuardState.RequireAttribute("diabetes_diagnosed"))
            .AddGuard(GuardState.AttributeGreaterThanOrEqual("severity", 3));

        // Act & Assert
        var act = () => scenario.Build();
        Should.Throw<InvalidOperationException>(act);
    }

    #endregion

    #region Time-Progressed Age Guards

    [Fact]
    public void GivenPatientAges_WhenGuardChecksAgeAfterDelay_ThenUsesCurrentAge()
    {
        // Act - Patient starts at 17, ages to 18+ after delay
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(age: 17)
            .AddEncounter("Visit before 18")
            .DelayMonths(24) // 2 years pass
            .AddGuard(GuardState.MinimumAge(18)) // Now patient is 19
            .AddEncounter("Visit after 18")
            .Build();

        // Assert
        scenario.Encounters.Count.ShouldBe(2);
        scenario.CurrentAge.ShouldBe(19);
    }

    #endregion
}
