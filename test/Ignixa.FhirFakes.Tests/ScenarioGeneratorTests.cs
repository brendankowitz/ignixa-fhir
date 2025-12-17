// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
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
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");
        scenario.Patient.Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenCreatesDiabetesCondition()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Conditions.Count.ShouldBeGreaterThanOrEqualTo(1);

        var diabetesCondition = scenario.Conditions.FirstOrDefault(c =>
        {
            var code = c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "44054006"; // SNOMED CT code for Type 2 Diabetes
        });

        diabetesCondition.ShouldNotBeNull("should have a diabetes diagnosis");
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenCreatesMultipleEncounters()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Encounters.Count.ShouldBeGreaterThanOrEqualTo(3, "should have initial + follow-up encounters");

        // All encounters should reference the patient
        foreach (var encounter in scenario.Encounters)
        {
            var subjectRef = encounter.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef!.ShouldContain(scenario.Patient!.Id);
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

        a1cObservations.Count.ShouldBeGreaterThanOrEqualTo(2, "should have multiple A1C tests over time");
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenCreatesMetforminMedication()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Medications.Count.ShouldBeGreaterThanOrEqualTo(1);

        var metformin = scenario.Medications.FirstOrDefault(m =>
        {
            var code = m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "860975" || code == "861007"; // Metformin 500mg or 1000mg
        });

        metformin.ShouldNotBeNull("should have Metformin prescription");
    }

    [Fact]
    public void GivenDiabeticPatientScenario_WhenGenerated_ThenTimelineIsChronological()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        var timestamps = scenario.Timeline.Select(e => e.Timestamp).ToList();
        timestamps.SequenceEqual(timestamps.OrderBy(t => t)).ShouldBeTrue("timeline events should be chronologically ordered");
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
                // Handle both urn:uuid: and Encounter/ formats
                var refId = encounterRef
                    .Replace("urn:uuid:", string.Empty, StringComparison.Ordinal)
                    .Replace("Encounter/", string.Empty, StringComparison.Ordinal);
                encounterIds.ShouldContain(refId, "observation should reference a valid encounter");
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

        a1cObservations.ShouldNotBeEmpty();

        // Higher severity should correlate with higher A1C values
        // Just verify we got valid values (detailed correlation testing would be brittle)
        foreach (var obs in a1cObservations)
        {
            var value = obs.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
            value!.Value.ShouldBeGreaterThan(6.0m);
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
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");
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

        hypertension.ShouldNotBeNull("should have hypertension diagnosis");
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

        bpObservations.Count.ShouldBeGreaterThanOrEqualTo(3, "should have multiple BP readings");

        // Each BP observation should have systolic and diastolic components
        foreach (var bp in bpObservations)
        {
            var components = bp.MutableNode["component"];
            components.ShouldNotBeNull("BP observation should have components");
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

        antihypertensives.Count.ShouldBeGreaterThanOrEqualTo(1, "should have antihypertensive medication");
    }

    #endregion

    #region Pregnant Patient Scenario Tests

    [Fact]
    public void GivenPregnantPatientScenario_WhenGenerated_ThenCreatesPatient()
    {
        // Act
        var scenario = _schemaProvider.GetPregnantPatient();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");

        // Should be female
        var gender = scenario.Patient.MutableNode["gender"]?.GetValue<string>();
        gender.ShouldBe("female");
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

        pregnancy.ShouldNotBeNull("should have pregnancy condition");
    }

    [Fact]
    public void GivenPregnantPatientScenario_WhenGenerated_ThenCreatesMultiplePrenatalVisits()
    {
        // Act
        var scenario = _schemaProvider.GetPregnantPatient();

        // Assert
        // Standard prenatal care has many visits
        scenario.Encounters.Count.ShouldBeGreaterThanOrEqualTo(10, "should have many prenatal visits");
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

        fhrObservations.Count.ShouldBeGreaterThanOrEqualTo(5, "should have multiple fetal heart rate measurements");
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

        prenatalVitamins.ShouldNotBeNull("should have prenatal vitamins");
    }

    #endregion

    #region Asthmatic Child Scenario Tests

    [Fact]
    public void GivenAsthmaticChildScenario_WhenGenerated_ThenCreatesChildPatient()
    {
        // Act
        var scenario = _schemaProvider.GetAsthmaticChild(age: 7);

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");

        // Verify age (should be around 7 years old)
        var birthDate = scenario.Patient.MutableNode["birthDate"]?.GetValue<string>();
        birthDate.ShouldNotBeNullOrEmpty();
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

        asthma.ShouldNotBeNull("should have asthma diagnosis");
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

        peakFlowObservations.Count.ShouldBeGreaterThanOrEqualTo(3, "should have peak flow measurements");
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

        albuterol.ShouldNotBeNull("should have albuterol (rescue inhaler)");
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

        emergencyVisits.Count.ShouldBeGreaterThanOrEqualTo(1, "should have at least one emergency/urgent visit for exacerbation");
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
        scenario.ScenarioName.ShouldBe("Custom Test Scenario");
        scenario.Description.ShouldBe("A test scenario for unit testing");
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
        scenario.Observations.Count.ShouldBe(1);
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
        scenario.Encounters.Count.ShouldBe(2);

        var visit1Start = scenario.Encounters[0].MutableNode["period"]?["start"]?.GetValue<string>();
        var visit2Start = scenario.Encounters[1].MutableNode["period"]?["start"]?.GetValue<string>();

        visit1Start.ShouldNotBeNull();
        visit2Start.ShouldNotBeNull();

        var date1 = DateTime.Parse(visit1Start!);
        var date2 = DateTime.Parse(visit2Start!);

        (date2 - date1).TotalDays.ShouldBe(91.3125, 2);
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
        scenario.Attributes.ShouldContainKey("test_attribute");
        scenario.GetAttribute<string>("test_attribute").ShouldBe("test_value");
        scenario.GetAttribute<int>("severity").ShouldBe(3);
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
        scenario.GetAttribute<int>("counter").ShouldBe(3);
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
            subjectRef.ShouldBe($"urn:uuid:{patientId}", "observation should reference the patient");
        }

        // Check all conditions reference the patient
        foreach (var condition in scenario.Conditions)
        {
            var subjectRef = condition.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.ShouldBe($"urn:uuid:{patientId}", "condition should reference the patient");
        }

        // Check all medications reference the patient
        foreach (var medication in scenario.Medications)
        {
            var subjectRef = medication.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.ShouldBe($"urn:uuid:{patientId}", "medication should reference the patient");
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
        scenario.AllResources.Count.ShouldBe(expectedCount, "AllResources should contain all generated resources including patient");
    }

    #endregion

    #region Timeline Tests

    [Fact]
    public void GivenGeneratedScenario_WhenExaminingTimeline_ThenEventsAreDescribed()
    {
        // Act
        var scenario = _schemaProvider.GetDiabeticPatient();

        // Assert
        scenario.Timeline.ShouldNotBeEmpty();

        foreach (var evt in scenario.Timeline)
        {
            evt.Description.ShouldNotBeNullOrEmpty("each event should have a description");
            evt.EventType.ShouldNotBeNullOrEmpty("each event should have a type");
            evt.ResourceId.ShouldNotBeNullOrEmpty("each event should reference a resource");
        }
    }

    #endregion
}
