// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for the Pediatric Ear Infection (Acute Otitis Media) scenario.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class EarInfectionScenarioTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Patient Tests

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenCreatesPatient()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        scenario.Patient.Should().NotBeNull();
        scenario.Patient!.ResourceType.Should().Be("Patient");
        scenario.Patient.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenGeneratedWithAge_ThenPatientHasCorrectAge()
    {
        // Arrange
        var specificAge = 6;

        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(age: specificAge);

        // Assert
        scenario.Patient.Should().NotBeNull();
        var birthDate = scenario.Patient!.MutableNode["birthDate"]?.GetValue<string>();
        birthDate.Should().NotBeNullOrEmpty("patient should have a birth date");

        // Verify the age is approximately correct (within 1 year tolerance)
        var birthDateTime = DateTime.Parse(birthDate!);
        var calculatedAge = (DateTime.UtcNow - birthDateTime).Days / 365;
        calculatedAge.Should().BeInRange(specificAge - 1, specificAge + 1);
    }

    #endregion

    #region Condition Tests

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenCreatesAcuteOtitisMediaCondition()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        scenario.Conditions.Should().HaveCountGreaterOrEqualTo(1);

        var otitisMediaCondition = scenario.Conditions.FirstOrDefault(c =>
        {
            var code = c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "7091009"; // SNOMED CT code for Acute otitis media
        });

        otitisMediaCondition.Should().NotBeNull("should have acute otitis media diagnosis");

        var display = otitisMediaCondition!.MutableNode["code"]?["coding"]?[0]?["display"]?.GetValue<string>();
        display.Should().Be("Acute otitis media");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenIncludesFollowUp_ThenConditionIsResolved()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(includeFollowUp: true);

        // Assert
        var otitisMediaCondition = scenario.Conditions.FirstOrDefault(c =>
        {
            var code = c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "7091009";
        });

        otitisMediaCondition.Should().NotBeNull();

        var clinicalStatus = otitisMediaCondition!.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        clinicalStatus.Should().Be("resolved", "condition should be resolved after follow-up");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenExcludesFollowUp_ThenConditionIsActive()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(includeFollowUp: false);

        // Assert
        var otitisMediaCondition = scenario.Conditions.FirstOrDefault(c =>
        {
            var code = c.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "7091009";
        });

        otitisMediaCondition.Should().NotBeNull();

        var clinicalStatus = otitisMediaCondition!.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        clinicalStatus.Should().Be("active", "condition should remain active without follow-up");
    }

    #endregion

    #region Encounter Tests

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenCreatesEncounters()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        scenario.Encounters.Should().HaveCountGreaterOrEqualTo(2, "should have initial visit and follow-up");

        // All encounters should reference the patient
        foreach (var encounter in scenario.Encounters)
        {
            var subjectRef = encounter.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Contain(scenario.Patient!.Id);
        }
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenExcludesFollowUp_ThenHasOnlyInitialEncounter()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(includeFollowUp: false);

        // Assert
        scenario.Encounters.Should().HaveCount(1, "should only have initial visit without follow-up");
    }

    #endregion

    #region Observation Tests

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenCreatesBodyTemperatureObservation()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        var temperatureObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "8310-5"; // LOINC code for Body temperature
        }).ToList();

        temperatureObservations.Should().NotBeEmpty("should have temperature observations");

        // Initial visit should show elevated temperature (fever)
        var initialTemp = temperatureObservations.First();
        var value = initialTemp.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value.Should().BeGreaterOrEqualTo(38.0m, "initial temperature should indicate fever");
        value.Should().BeLessOrEqualTo(39.5m, "temperature should be in expected range");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenIncludesFollowUp_ThenTemperatureNormalizesAfterTreatment()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(includeFollowUp: true);

        // Assert
        var temperatureObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "8310-5";
        }).ToList();

        temperatureObservations.Should().HaveCountGreaterOrEqualTo(2, "should have initial and follow-up temperatures");

        // Follow-up should show normal temperature
        var followUpTemp = temperatureObservations.Last();
        var value = followUpTemp.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value.Should().BeLessOrEqualTo(37.5m, "follow-up temperature should be normal or near-normal");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenCreatesPainSeverityObservation()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        var painObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "72514-3"; // LOINC code for Pain severity
        }).ToList();

        painObservations.Should().NotBeEmpty("should have pain severity observations");

        // Initial visit should show moderate to severe pain
        var initialPain = painObservations.First();
        var value = initialPain.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value.Should().BeGreaterOrEqualTo(5m, "initial pain should be moderate to severe");
        value.Should().BeLessOrEqualTo(8m, "pain should be in expected range");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenIncludesFollowUp_ThenPainResolvesAfterTreatment()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(includeFollowUp: true);

        // Assert
        var painObservations = scenario.Observations.Where(o =>
        {
            var code = o.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "72514-3";
        }).ToList();

        painObservations.Should().HaveCountGreaterOrEqualTo(2, "should have initial and follow-up pain assessments");

        // Follow-up should show minimal or no pain
        var followUpPain = painObservations.Last();
        var value = followUpPain.MutableNode["valueQuantity"]?["value"]?.GetValue<decimal>();
        value.Should().BeLessOrEqualTo(1m, "follow-up pain should be minimal or resolved");
    }

    #endregion

    #region Procedure Tests

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenCreatesOtoscopyProcedure()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        var otoscopyProcedures = scenario.Procedures.Where(p =>
        {
            var code = p.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "16247007"; // SNOMED CT code for Otoscopy
        }).ToList();

        otoscopyProcedures.Should().NotBeEmpty("should have otoscopy examination");

        var initialOtoscopy = otoscopyProcedures.First();
        var display = initialOtoscopy.MutableNode["code"]?["coding"]?[0]?["display"]?.GetValue<string>();
        display.Should().Be("Otoscopy");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenIncludesFollowUp_ThenCreatesFollowUpOtoscopy()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(includeFollowUp: true);

        // Assert
        var otoscopyProcedures = scenario.Procedures.Where(p =>
        {
            var code = p.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "16247007";
        }).ToList();

        otoscopyProcedures.Should().HaveCountGreaterOrEqualTo(2, "should have initial and follow-up examinations");

        // Follow-up should indicate resolution
        var followUpOtoscopy = otoscopyProcedures.Last();
        var outcome = followUpOtoscopy.MutableNode["outcome"]?["text"]?.GetValue<string>();
        outcome.Should().Contain("normal", "follow-up should show improvement");
    }

    #endregion

    #region Medication Tests

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenCreatesAmoxicillinMedication()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        scenario.Medications.Should().HaveCountGreaterOrEqualTo(1);

        var amoxicillin = scenario.Medications.FirstOrDefault(m =>
        {
            var code = m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "308192"; // RxNorm code for Amoxicillin 500mg
        });

        amoxicillin.Should().NotBeNull("should have Amoxicillin prescription");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenAmoxicillinHasCorrectDosing()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        var amoxicillin = scenario.Medications.FirstOrDefault(m =>
        {
            var code = m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "308192";
        });

        amoxicillin.Should().NotBeNull();

        // Check frequency (twice daily)
        var dosageInstruction = amoxicillin!.MutableNode["dosageInstruction"]?[0];
        dosageInstruction.Should().NotBeNull();

        var timing = dosageInstruction!["timing"]?["repeat"];
        timing.Should().NotBeNull();

        var frequency = timing!["frequency"]?.GetValue<int>();
        frequency.Should().Be(2, "should be twice daily");

        // Check that it's not chronic (10-day course)
        var dispenseRequest = amoxicillin.MutableNode["dispenseRequest"];
        var numberOfRepeats = dispenseRequest?["numberOfRepeatsAllowed"]?.GetValue<int>();
        numberOfRepeats.Should().Be(0, "should not have repeats for acute treatment");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenMedicationReferencesCondition()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        var amoxicillin = scenario.Medications.FirstOrDefault(m =>
        {
            var code = m.MutableNode["medicationCodeableConcept"]?["coding"]?[0]?["code"]?.GetValue<string>();
            return code == "308192";
        });

        amoxicillin.Should().NotBeNull();

        var reasonReference = amoxicillin!.MutableNode["reasonReference"];
        reasonReference.Should().NotBeNull("medication should reference the condition");
    }

    #endregion

    #region Timeline and Reference Tests

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenTimelineIsChronological()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();

        // Assert
        var timestamps = scenario.Timeline.Select(e => e.Timestamp).ToList();
        timestamps.Should().BeInAscendingOrder("timeline events should be chronologically ordered");
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenGenerated_ThenAllReferencesAreValid()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection();
        var patientId = scenario.Patient!.Id;

        // Assert - All resources reference the patient
        foreach (var observation in scenario.Observations)
        {
            var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Be($"urn:uuid:{patientId}", "observation should reference the patient");
        }

        foreach (var condition in scenario.Conditions)
        {
            var subjectRef = condition.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Be($"urn:uuid:{patientId}", "condition should reference the patient");
        }

        foreach (var medication in scenario.Medications)
        {
            var subjectRef = medication.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Be($"urn:uuid:{patientId}", "medication should reference the patient");
        }

        foreach (var procedure in scenario.Procedures)
        {
            var subjectRef = procedure.MutableNode["subject"]?["reference"]?.GetValue<string>();
            subjectRef.Should().Be($"urn:uuid:{patientId}", "procedure should reference the patient");
        }
    }

    [Fact]
    public void GivenEarInfectionScenario_WhenIncludesFollowUp_ThenFollowUpOccursAfter10Days()
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(includeFollowUp: true);

        // Assert
        scenario.Encounters.Should().HaveCount(2);

        var initialVisit = scenario.Encounters[0].MutableNode["period"]?["start"]?.GetValue<string>();
        var followUpVisit = scenario.Encounters[1].MutableNode["period"]?["start"]?.GetValue<string>();

        initialVisit.Should().NotBeNullOrEmpty();
        followUpVisit.Should().NotBeNullOrEmpty();

        var date1 = DateTime.Parse(initialVisit!);
        var date2 = DateTime.Parse(followUpVisit!);

        var daysDifference = (date2 - date1).TotalDays;
        daysDifference.Should().BeApproximately(10, 1, "follow-up should be approximately 10 days after initial visit");
    }

    #endregion

    #region Parameterized Tests

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(10)]
    public void GivenEarInfectionScenario_WhenGeneratedWithDifferentAges_ThenCreatesValidScenario(int age)
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(age: age);

        // Assert
        scenario.Patient.Should().NotBeNull();
        scenario.Conditions.Should().NotBeEmpty();
        scenario.Observations.Should().NotBeEmpty();
        scenario.Procedures.Should().NotBeEmpty();
        scenario.Medications.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("male")]
    [InlineData("female")]
    public void GivenEarInfectionScenario_WhenGeneratedWithGender_ThenPatientHasCorrectGender(string gender)
    {
        // Act
        var scenario = _schemaProvider.GetPediatricEarInfection(gender: gender);

        // Assert
        var patientGender = scenario.Patient!.MutableNode["gender"]?.GetValue<string>();
        patientGender.Should().Be(gender);
    }

    #endregion
}
