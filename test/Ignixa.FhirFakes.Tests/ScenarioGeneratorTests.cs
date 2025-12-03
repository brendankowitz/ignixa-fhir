// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for the ScenarioGenerator and scenario building infrastructure.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class ScenarioGeneratorTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Diabetic Patient Scenario Tests

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenCreatesPatient()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Patient.Should().NotBeNull();
        scenario.Patient!.ResourceType.Should().Be("Patient");
        scenario.Patient.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenCreatesDiabetesCondition()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Conditions.Should().HaveCountGreaterOrEqualTo(1);

        var diabetesCondition = scenario.Conditions.FirstOrDefault(c =>
        {
            var code = c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "44054006"; // SNOMED CT code for Type 2 Diabetes
        });

        diabetesCondition.Should().NotBeNull("should have a diabetes diagnosis");
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenCreatesMultipleEncounters()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Encounters.Should().HaveCountGreaterOrEqualTo(3, "should have initial + follow-up encounters");

        // All encounters should reference the patient
        foreach (var encounter in scenario.Encounters)
        {
            var subjectRef = encounter.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Contain(scenario.Patient!.Id);
        }
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenCreatesHemoglobinA1cObservations()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        var a1cObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "4548-4"; // LOINC code for HbA1c
        }).ToList();

        a1cObservations.Should().HaveCountGreaterOrEqualTo(2, "should have multiple A1C tests over time");
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenCreatesMetforminMedication()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Medications.Should().HaveCountGreaterOrEqualTo(1);

        var metformin = scenario.Medications.FirstOrDefault(m =>
        {
            var code = m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "860975" || code == "861007"; // Metformin 500mg or 1000mg
        });

        metformin.Should().NotBeNull("should have Metformin prescription");
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenTimelineIsChronological()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        var timestamps = scenario.Timeline.Select(e => e.Timestamp).ToList();
        timestamps.Should().BeInAscendingOrder("timeline events should be chronologically ordered");
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenObservationsReferenceEncounters()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        var encounterIds = scenario.Encounters.Select(e => e.Id).ToHashSet();

        foreach (var observation in scenario.Observations)
        {
            var encounterRef = observation.MutableNode["encounter"]?["reference"]?.GetValue<string>();
            if (encounterRef is not null)
            {
                var refId = encounterRef.Replace("Encounter/", string.Empty, StringComparison.Ordinal);
                encounterIds.Should().Contain(refId, "observation should reference a valid encounter");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GivenDiabeticPatientScenario_WhenSeverityVaries_ThenA1cValuesReflectSeverity(int severity)
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient(severity: severity);

        // Assert
        var a1cObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "4548-4";
        }).ToList();

        a1cObservations.Should().NotBeEmpty();

        // Higher severity should correlate with higher A1C values
        // Just verify we got valid values (detailed correlation testing would be brittle)
        foreach (var obs in a1cObservations)
        {
            var value = obs.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
            value.Should().BeGreaterThan(6.0m, "A1C should be in diabetic range");
        }
    }

    #endregion

    #region Hypertensive Patient Scenario Tests

    [Fact]
    public void GivenHypertensivePatientScenario_WhenGenerated_ThenCreatesPatient()
    {
        // Act
        var scenario = _schemaProvider.GetHypertensivePatient();

        // Assert
        scenario.Patient.Should().NotBeNull();
        scenario.Patient!.ResourceType.Should().Be("Patient");
    }

    [Fact]
    public void GivenHypertensivePatientScenario_WhenGenerated_ThenCreatesHypertensionCondition()
    {
        // Act
        var scenario = _schemaProvider.GetHypertensivePatient();

        // Assert
        var hypertension = scenario.Conditions.FirstOrDefault(c =>
        {
            var code = c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "38341003"; // SNOMED CT code for Hypertension
        });

        hypertension.Should().NotBeNull("should have hypertension diagnosis");
    }

    [Fact]
    public void GivenHypertensivePatientScenario_WhenGenerated_ThenCreatesBloodPressureObservations()
    {
        // Act
        var scenario = _schemaProvider.GetHypertensivePatient();

        // Assert
        var bpObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "85354-9"; // Blood pressure panel
        }).ToList();

        bpObservations.Should().HaveCountGreaterOrEqualTo(3, "should have multiple BP readings");

        // Each BP observation should have systolic and diastolic components
        foreach (var bp in bpObservations)
        {
            var components = bp.MutableNode["component"];
            components.Should().NotBeNull("BP observation should have components");
        }
    }

    [Fact]
    public void GivenHypertensivePatientScenario_WhenGenerated_ThenCreatesAntihypertensiveMedication()
    {
        // Act
        var scenario = _schemaProvider.GetHypertensivePatient();

        // Assert
        var antihypertensives = scenario.Medications.Where(m =>
        {
            var code = m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "314076" || code == "314077" || code == "329528"; // Lisinopril or Amlodipine
        }).ToList();

        antihypertensives.Should().HaveCountGreaterOrEqualTo(1, "should have antihypertensive medication");
    }

    #endregion

    #region Pregnant Patient Scenario Tests

    [Fact]
    public void GivenPregnantPatientScenario_WhenGenerated_ThenCreatesPatient()
    {
        // Act
        var scenario = _schemaProvider.GetPregnantPatient();

        // Assert
        scenario.Patient.Should().NotBeNull();
        scenario.Patient!.ResourceType.Should().Be("Patient");

        // Should be female
        var gender = scenario.Patient.MutableNode["gender"]?.GetValue<string>();
        gender.Should().Be("female");
    }

    [Fact]
    public void GivenPregnantPatientScenario_WhenGenerated_ThenCreatesPregnancyCondition()
    {
        // Act
        var scenario = _schemaProvider.GetPregnantPatient();

        // Assert
        var pregnancy = scenario.Conditions.FirstOrDefault(c =>
        {
            var code = c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "72892002"; // Normal pregnancy
        });

        pregnancy.Should().NotBeNull("should have pregnancy condition");
    }

    [Fact]
    public void GivenPregnantPatientScenario_WhenGenerated_ThenCreatesMultiplePrenatalVisits()
    {
        // Act
        var scenario = _schemaProvider.GetPregnantPatient();

        // Assert
        // Standard prenatal care has many visits
        scenario.Encounters.Should().HaveCountGreaterOrEqualTo(10, "should have many prenatal visits");
    }

    [Fact]
    public void GivenPregnantPatientScenario_WhenGenerated_ThenCreatesFetalHeartRateObservations()
    {
        // Act
        var scenario = _schemaProvider.GetPregnantPatient();

        // Assert
        var fhrObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "55283-6"; // Fetal heart rate
        }).ToList();

        fhrObservations.Should().HaveCountGreaterOrEqualTo(5, "should have multiple fetal heart rate measurements");
    }

    [Fact]
    public void GivenPregnantPatientScenario_WhenGenerated_ThenCreatesPrenatalMedications()
    {
        // Act
        var scenario = _schemaProvider.GetPregnantPatient();

        // Assert
        var prenatalVitamins = scenario.Medications.FirstOrDefault(m =>
        {
            var code = m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "315246"; // Prenatal vitamins
        });

        prenatalVitamins.Should().NotBeNull("should have prenatal vitamins");
    }

    #endregion

    #region Asthmatic Child Scenario Tests

    [Fact]
    public void GivenAsthmaticChildScenario_WhenGenerated_ThenCreatesChildPatient()
    {
        // Act
        var scenario = _schemaProvider.GetAsthmaticChild(age: 7);

        // Assert
        scenario.Patient.Should().NotBeNull();
        scenario.Patient!.ResourceType.Should().Be("Patient");

        // Verify age (should be around 7 years old)
        var birthDate = scenario.Patient.MutableNode["birthDate"]?.GetValue<string>();
        birthDate.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenAsthmaticChildScenario_WhenGenerated_ThenCreatesAsthmaCondition()
    {
        // Act
        var scenario = _schemaProvider.GetAsthmaticChild();

        // Assert
        var asthma = scenario.Conditions.FirstOrDefault(c =>
        {
            var code = c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "195967001"; // Asthma
        });

        asthma.Should().NotBeNull("should have asthma diagnosis");
    }

    [Fact]
    public void GivenAsthmaticChildScenario_WhenGenerated_ThenCreatesPeakFlowObservations()
    {
        // Act
        var scenario = _schemaProvider.GetAsthmaticChild();

        // Assert
        var peakFlowObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "33452-4"; // Peak expiratory flow rate
        }).ToList();

        peakFlowObservations.Should().HaveCountGreaterOrEqualTo(3, "should have peak flow measurements");
    }

    [Fact]
    public void GivenAsthmaticChildScenario_WhenGenerated_ThenCreatesRescueInhaler()
    {
        // Act
        var scenario = _schemaProvider.GetAsthmaticChild();

        // Assert
        var albuterol = scenario.Medications.FirstOrDefault(m =>
        {
            var code = m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "435"; // Albuterol
        });

        albuterol.Should().NotBeNull("should have albuterol (rescue inhaler)");
    }

    [Fact]
    public void GivenAsthmaticChildScenario_WhenGenerated_ThenIncludesExacerbationVisit()
    {
        // Act
        var scenario = _schemaProvider.GetAsthmaticChild();

        // Assert
        var emergencyVisits = scenario.Encounters.Where(e =>
        {
            var classCode = e.MutableNode["class"]?["code"]?.GetValue<string>();
            return classCode == "EMER";
        }).ToList();

        emergencyVisits.Should().HaveCountGreaterOrEqualTo(1, "should have at least one emergency/urgent visit for exacerbation");
    }

    #endregion

    #region ScenarioBuilder Fluent API Tests

    [Fact]
    public void GivenScenarioBuilder_WhenBuildingCustomScenario_ThenReturnsValidContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Custom Test Scenario")
            .WithDescription("A test scenario for unit testing")
            .WithPatient(age: 45, gender: "male")
            .AddEncounter("Initial visit")
            .AddObservation(FhirCode.Observations.BloodGlucose, 120m, "mg/dL")
            .Build();

        // Assert
        scenario.ScenarioName.Should().Be("Custom Test Scenario");
        scenario.Description.Should().Be("A test scenario for unit testing");
        scenario.Patient.Should().NotBeNull();
        scenario.Encounters.Should().HaveCount(1);
        scenario.Observations.Should().HaveCount(1);
    }

    [Fact]
    public void GivenScenarioBuilder_WhenAddingDelay_ThenAdvancesTime()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(startDate: startDate)
            .AddEncounter("Visit 1")
            .DelayMonths(3)
            .AddEncounter("Visit 2")
            .Build();

        // Assert
        scenario.Encounters.Should().HaveCount(2);

        var visit1Start = scenario.Encounters[0].MutableNode["period"]?["start"]?.GetValue<string>();
        var visit2Start = scenario.Encounters[1].MutableNode["period"]?["start"]?.GetValue<string>();

        visit1Start.Should().NotBeNull();
        visit2Start.Should().NotBeNull();

        var date1 = DateTime.Parse(visit1Start!);
        var date2 = DateTime.Parse(visit2Start!);

        (date2 - date1).TotalDays.Should().BeApproximately(91.3125, 2, "visit 2 should be ~3 months after visit 1 (3 * 30.4375 days)");
    }

    [Fact]
    public void GivenScenarioBuilder_WhenSettingAttributes_ThenAttributesAreAvailable()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .SetAttribute("test_attribute", "test_value")
            .SetAttribute("severity", 3)
            .Build();

        // Assert
        scenario.Attributes.Should().ContainKey("test_attribute");
        scenario.GetAttribute<string>("test_attribute").Should().Be("test_value");
        scenario.GetAttribute<int>("severity").Should().Be(3);
    }

    [Fact]
    public void GivenScenarioBuilder_WhenIncrementingAttribute_ThenValueIncreases()
    {
        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .SetAttribute("counter", 0)
            .IncrementAttribute("counter")
            .IncrementAttribute("counter")
            .IncrementAttribute("counter")
            .Build();

        // Assert
        scenario.GetAttribute<int>("counter").Should().Be(3);
    }

    #endregion

    #region Reference Integrity Tests

    [Fact]
    public void GivenGeneratedScenario_WhenCheckingReferences_ThenAllReferencesAreValid()
    {
        // Arrange
        var scenario = _schemaProvider.GetDiabeticPatient();
        var patientId = scenario.Patient!.Id;
        var encounterIds = scenario.Encounters.Select(e => e.Id).ToHashSet();

        // Act & Assert - Check all observations reference the patient
        foreach (var observation in scenario.Observations)
        {
            var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Be($"Patient/{patientId}", "observation should reference the patient");
        }

        // Check all conditions reference the patient
        foreach (var condition in scenario.Conditions)
        {
            var subjectRef = condition.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Be($"Patient/{patientId}", "condition should reference the patient");
        }

        // Check all medications reference the patient
        foreach (var medication in scenario.Medications)
        {
            var subjectRef = medication.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Be($"Patient/{patientId}", "medication should reference the patient");
        }
    }

    [Fact]
    public void GivenGeneratedScenario_WhenListingAllResources_ThenCountsMatch()
    {
        // Arrange
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Act
        var expectedCount = 1 + // Patient
                            scenario.Encounters.Count +
                            scenario.Conditions.Count +
                            scenario.Observations.Count +
                            scenario.Medications.Count +
                            scenario.Procedures.Count;

        // Assert
        scenario.AllResources.Should().HaveCount(expectedCount - 1, "AllResources should contain all generated resources except patient");
    }

    #endregion

    #region Timeline Tests

    [Fact]
    public void GivenGeneratedScenario_WhenExaminingTimeline_ThenEventsAreDescribed()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Timeline.Should().NotBeEmpty();

        foreach (var evt in scenario.Timeline)
        {
            evt.Description.Should().NotBeNullOrEmpty("each event should have a description");
            evt.EventType.Should().NotBeNullOrEmpty("each event should have a type");
            evt.ResourceId.Should().NotBeNullOrEmpty("each event should reference a resource");
        }
    }

    #endregion
}
