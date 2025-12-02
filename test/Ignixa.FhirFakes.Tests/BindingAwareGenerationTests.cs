// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for binding-aware data generation in SchemaBasedFhirResourceFaker.
/// Verifies that the faker uses ITypeExtended binding information to generate
/// terminology-correct codes instead of relying solely on property name heuristics.
/// </summary>
public class BindingAwareGenerationTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly SchemaBasedFhirResourceFaker _faker;

    public BindingAwareGenerationTests()
    {
        _faker = new SchemaBasedFhirResourceFaker(_schemaProvider);
    }

    #region BindingCodeMapper Tests

    [Fact]
    public void BindingCodeMapper_GivenAdministrativeGenderValueSet_ThenReturnsGenderCodes()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            out var codes);

        // Assert
        result.Should().BeTrue();
        codes.Should().HaveCount(4);
        codes.Should().Contain(c => c.Code == "male");
        codes.Should().Contain(c => c.Code == "female");
        codes.Should().Contain(c => c.Code == "other");
        codes.Should().Contain(c => c.Code == "unknown");
    }

    [Fact]
    public void BindingCodeMapper_GivenObservationCodesValueSet_ThenReturnsLoincCodes()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://hl7.org/fhir/ValueSet/observation-codes",
            out var codes);

        // Assert
        result.Should().BeTrue();
        codes.Should().NotBeEmpty();
        codes.Should().Contain(c => c.System == FhirCode.Systems.Loinc);
    }

    [Fact]
    public void BindingCodeMapper_GivenProcedureCodeValueSet_ThenReturnsSnomedCodes()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://hl7.org/fhir/ValueSet/procedure-code",
            out var codes);

        // Assert
        result.Should().BeTrue();
        codes.Should().NotBeEmpty();
        codes.Should().Contain(c => c.System == FhirCode.Systems.SnomedCt);
        codes.Should().Contain(Procedures.Appendectomy);
        codes.Should().Contain(Procedures.Colonoscopy);
    }

    [Fact]
    public void BindingCodeMapper_GivenAllergyIntoleranceCodeValueSet_ThenReturnsAllergenCodes()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://hl7.org/fhir/ValueSet/allergyintolerance-code",
            out var codes);

        // Assert
        result.Should().BeTrue();
        codes.Should().NotBeEmpty();
        codes.Should().Contain(Allergens.Peanuts);
        codes.Should().Contain(Allergens.Penicillin);
        codes.Should().Contain(Allergens.Shellfish);
    }

    [Fact]
    public void BindingCodeMapper_GivenVaccineCodeValueSet_ThenReturnsCvxCodes()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://hl7.org/fhir/ValueSet/vaccine-code",
            out var codes);

        // Assert
        result.Should().BeTrue();
        codes.Should().NotBeEmpty();
        codes.Should().Contain(c => c.System == FhirCode.Systems.Cvx);
        codes.Should().Contain(Immunizations.MMR);
        codes.Should().Contain(Immunizations.DTaP);
        codes.Should().Contain(Immunizations.Influenza);
    }

    [Fact]
    public void BindingCodeMapper_GivenMedicationCodesValueSet_ThenReturnsRxNormCodes()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://hl7.org/fhir/ValueSet/medication-codes",
            out var codes);

        // Assert
        result.Should().BeTrue();
        codes.Should().NotBeEmpty();
        codes.Should().Contain(c => c.System == FhirCode.Systems.RxNorm);
    }

    [Fact]
    public void BindingCodeMapper_GivenObservationStatusValueSet_ThenReturnsStatusCodes()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://hl7.org/fhir/ValueSet/observation-status",
            out var codes);

        // Assert
        result.Should().BeTrue();
        codes.Should().Contain(c => c.Code == "final");
        codes.Should().Contain(c => c.Code == "preliminary");
        codes.Should().Contain(c => c.Code == "registered");
    }

    [Fact]
    public void BindingCodeMapper_GivenVersionedValueSetUri_ThenNormalizesAndReturnsMatch()
    {
        // Value set URIs can include version suffixes like "|4.0.1"
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://hl7.org/fhir/ValueSet/administrative-gender|4.0.1",
            out var codes);

        // Assert
        result.Should().BeTrue();
        codes.Should().HaveCount(4);
    }

    [Fact]
    public void BindingCodeMapper_GivenUnknownValueSet_ThenReturnsFalse()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(
            "http://example.org/ValueSet/unknown",
            out var codes);

        // Assert
        result.Should().BeFalse();
        codes.Should().BeEmpty();
    }

    [Fact]
    public void BindingCodeMapper_GivenNullValueSet_ThenReturnsFalse()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(null, out var codes);

        // Assert
        result.Should().BeFalse();
        codes.Should().BeEmpty();
    }

    [Fact]
    public void BindingCodeMapper_GivenEmptyValueSet_ThenReturnsFalse()
    {
        // Act
        var result = BindingCodeMapper.TryGetCodesForValueSet(string.Empty, out var codes);

        // Assert
        result.Should().BeFalse();
        codes.Should().BeEmpty();
    }

    #endregion

    #region GetAll* Method Tests

    [Fact]
    public void BindingCodeMapper_GetAllAllergenCodes_ThenReturnsAllAllergens()
    {
        // Act
        var codes = BindingCodeMapper.GetAllAllergenCodes();

        // Assert
        codes.Should().NotBeEmpty();
        codes.Should().Contain(Allergens.Peanuts);
        codes.Should().Contain(Allergens.Penicillin);
        codes.Should().Contain(Allergens.Latex);
        codes.Should().AllSatisfy(c => c.System.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public void BindingCodeMapper_GetAllImmunizationCodes_ThenReturnsAllVaccines()
    {
        // Act
        var codes = BindingCodeMapper.GetAllImmunizationCodes();

        // Assert
        codes.Should().NotBeEmpty();
        codes.Should().Contain(Immunizations.MMR);
        codes.Should().Contain(Immunizations.Covid19Pfizer);
        codes.Should().AllSatisfy(c => c.System.Should().Be(FhirCode.Systems.Cvx));
    }

    [Fact]
    public void BindingCodeMapper_GetAllLabObservationCodes_ThenReturnsAllLabCodes()
    {
        // Act
        var codes = BindingCodeMapper.GetAllLabObservationCodes();

        // Assert
        codes.Should().NotBeEmpty();
        codes.Should().Contain(LabObservations.Glucose);
        codes.Should().Contain(LabObservations.Hemoglobin);
        codes.Should().Contain(LabObservations.HemoglobinA1c);
        codes.Should().AllSatisfy(c => c.System.Should().Be(FhirCode.Systems.Loinc));
    }

    [Fact]
    public void BindingCodeMapper_GetAllProcedureCodes_ThenReturnsAllProcedures()
    {
        // Act
        var codes = BindingCodeMapper.GetAllProcedureCodes();

        // Assert
        codes.Should().NotBeEmpty();
        codes.Should().Contain(Procedures.Appendectomy);
        codes.Should().Contain(Procedures.TotalKneeReplacement);
        codes.Should().Contain(Procedures.CABG);
        codes.Should().AllSatisfy(c => c.System.Should().Be(FhirCode.Systems.SnomedCt));
    }

    [Fact]
    public void BindingCodeMapper_GetAllVitalSignCodes_ThenReturnsAllVitals()
    {
        // Act
        var codes = BindingCodeMapper.GetAllVitalSignCodes();

        // Assert
        codes.Should().NotBeEmpty();
        codes.Should().Contain(VitalSigns.BodyTemperature);
        codes.Should().Contain(VitalSigns.HeartRate);
        codes.Should().Contain(VitalSigns.BloodPressurePanel);
        codes.Should().AllSatisfy(c => c.System.Should().Be(FhirCode.Systems.Loinc));
    }

    [Fact]
    public void BindingCodeMapper_GetAllDiagnosticReportCodes_ThenReturnsAllReportTypes()
    {
        // Act
        var codes = BindingCodeMapper.GetAllDiagnosticReportCodes();

        // Assert
        codes.Should().NotBeEmpty();
        codes.Should().Contain(DiagnosticReports.ComprehensiveMetabolicPanel);
        codes.Should().Contain(DiagnosticReports.CompleteBloodCount);
        codes.Should().AllSatisfy(c => c.System.Should().Be(FhirCode.Systems.Loinc));
    }

    #endregion

    #region Binding Strength Tests

    [Theory]
    [InlineData("required", true)]
    [InlineData("extensible", true)]
    [InlineData("preferred", false)]
    [InlineData("example", false)]
    public void BindingCodeMapper_IsStrictBinding_ThenReturnsExpectedResult(string strength, bool expectedStrict)
    {
        // Act
        var result = BindingCodeMapper.IsStrictBinding(strength);

        // Assert
        result.Should().Be(expectedStrict);
    }

    [Theory]
    [InlineData("required", true)]
    [InlineData("extensible", false)]
    [InlineData("preferred", false)]
    [InlineData("example", false)]
    public void BindingCodeMapper_IsRequiredBinding_ThenReturnsExpectedResult(string strength, bool expectedRequired)
    {
        // Act
        var result = BindingCodeMapper.IsRequiredBinding(strength);

        // Assert
        result.Should().Be(expectedRequired);
    }

    #endregion

    #region SchemaBasedFhirResourceFaker Integration Tests

    [Fact]
    public void GeneratePatient_WhenGenderHasBinding_ThenUsesValidGenderCode()
    {
        // Arrange & Act
        var patient = _faker.Generate("Patient");

        // Assert
        var gender = patient.MutableNode["gender"]?.GetValue<string>();
        if (gender is not null)
        {
            gender.Should().BeOneOf("male", "female", "other", "unknown",
                "Patient.gender should use administrative-gender value set");
        }
    }

    [Fact]
    public void GeneratePatient_WhenNameUseHasBinding_ThenUsesValidNameUseCode()
    {
        // Arrange & Act
        var patient = _faker.Generate("Patient");

        // Assert
        var name = patient.MutableNode["name"]?[0];
        var use = name?["use"]?.GetValue<string>();
        if (use is not null)
        {
            use.Should().BeOneOf("usual", "official", "temp", "nickname", "anonymous", "old", "maiden",
                "HumanName.use should use name-use value set");
        }
    }

    [Fact]
    public void GeneratePatient_WhenAddressUseHasBinding_ThenUsesValidAddressUseCode()
    {
        // Arrange & Act
        var patient = _faker.Generate("Patient");

        // Assert
        var address = patient.MutableNode["address"]?[0];
        var use = address?["use"]?.GetValue<string>();
        if (use is not null)
        {
            use.Should().BeOneOf("home", "work", "temp", "old", "billing",
                "Address.use should use address-use value set");
        }
    }

    [Fact]
    public void GeneratePatient_WhenTelecomSystemHasBinding_ThenUsesValidContactPointSystemCode()
    {
        // Arrange & Act
        var patient = _faker.Generate("Patient");

        // Assert
        var telecom = patient.MutableNode["telecom"]?[0];
        var system = telecom?["system"]?.GetValue<string>();
        if (system is not null)
        {
            system.Should().BeOneOf("phone", "fax", "email", "pager", "url", "sms", "other",
                "ContactPoint.system should use contact-point-system value set");
        }
    }

    [Fact]
    public void GeneratePatient_WhenMaritalStatusHasBinding_ThenUsesValidMaritalStatusCode()
    {
        // Arrange & Act
        var patient = _faker.Generate("Patient");

        // Assert
        var maritalStatus = patient.MutableNode["maritalStatus"];
        if (maritalStatus is not null)
        {
            var coding = maritalStatus["coding"]?[0];
            var code = coding?["code"]?.GetValue<string>();
            if (code is not null)
            {
                // Valid marital status codes from v3-MaritalStatus
                code.Should().BeOneOf("A", "D", "I", "L", "M", "P", "S", "T", "U", "W", "UNK",
                    "MaritalStatus should use marital-status value set");
            }
        }
    }

    [Fact]
    public void GenerateObservation_WhenStatusHasBinding_ThenUsesValidObservationStatusCode()
    {
        // Arrange & Act
        var observation = _faker.Generate("Observation");

        // Assert
        var status = observation.MutableNode["status"]?.GetValue<string>();
        if (status is not null)
        {
            status.Should().BeOneOf(
                "registered", "preliminary", "final", "amended", "corrected",
                "cancelled", "entered-in-error", "unknown",
                "Observation.status should use observation-status value set");
        }
    }

    [Fact]
    public void GenerateCondition_WhenClinicalStatusHasBinding_ThenUsesValidClinicalStatusCode()
    {
        // Arrange & Act
        var condition = _faker.Generate("Condition");

        // Assert
        var clinicalStatus = condition.MutableNode["clinicalStatus"];
        if (clinicalStatus is not null)
        {
            var coding = clinicalStatus["coding"]?[0];
            var code = coding?["code"]?.GetValue<string>();
            if (code is not null)
            {
                code.Should().BeOneOf("active", "recurrence", "relapse", "inactive", "remission", "resolved",
                    "Condition.clinicalStatus should use condition-clinical value set");
            }
        }
    }

    [Fact]
    public void GenerateEncounter_WhenStatusHasBinding_ThenUsesValidEncounterStatusCode()
    {
        // Arrange & Act
        var encounter = _faker.Generate("Encounter");

        // Assert
        var status = encounter.MutableNode["status"]?.GetValue<string>();
        if (status is not null)
        {
            status.Should().BeOneOf(
                "planned", "arrived", "triaged", "in-progress", "onleave",
                "finished", "cancelled", "entered-in-error", "unknown",
                "Encounter.status should use encounter-status value set");
        }
    }

    [Fact]
    public void GenerateAllergyIntolerance_WhenClinicalStatusHasBinding_ThenUsesValidClinicalStatusCode()
    {
        // Arrange & Act
        var allergy = _faker.Generate("AllergyIntolerance");

        // Assert
        var clinicalStatus = allergy.MutableNode["clinicalStatus"];
        if (clinicalStatus is not null)
        {
            var coding = clinicalStatus["coding"]?[0];
            var code = coding?["code"]?.GetValue<string>();
            if (code is not null)
            {
                code.Should().BeOneOf("active", "inactive", "resolved",
                    "AllergyIntolerance.clinicalStatus should use allergyintolerance-clinical value set");
            }
        }
    }

    [Fact]
    public void GenerateAllergyIntolerance_WhenTypeHasBinding_ThenUsesValidTypeCode()
    {
        // Arrange & Act
        var allergy = _faker.Generate("AllergyIntolerance");

        // Assert
        var type = allergy.MutableNode["type"]?.GetValue<string>();
        if (type is not null)
        {
            type.Should().BeOneOf("allergy", "intolerance",
                "AllergyIntolerance.type should use allergy-intolerance-type value set");
        }
    }

    [Fact]
    public void GenerateAllergyIntolerance_WhenCategoryHasBinding_ThenUsesValidCategoryCode()
    {
        // Arrange & Act
        var allergy = _faker.Generate("AllergyIntolerance");

        // Assert
        var category = allergy.MutableNode["category"]?[0]?.GetValue<string>();
        if (category is not null)
        {
            category.Should().BeOneOf("food", "medication", "environment", "biologic",
                "AllergyIntolerance.category should use allergy-intolerance-category value set");
        }
    }

    [Fact]
    public void GenerateProcedure_WhenStatusHasBinding_ThenUsesValidEventStatusCode()
    {
        // Arrange & Act
        var procedure = _faker.Generate("Procedure");

        // Assert
        var status = procedure.MutableNode["status"]?.GetValue<string>();
        if (status is not null)
        {
            status.Should().BeOneOf(
                "preparation", "in-progress", "not-done", "on-hold",
                "stopped", "completed", "entered-in-error", "unknown",
                "Procedure.status should use event-status value set");
        }
    }

    [Fact]
    public void GenerateMedicationRequest_WhenStatusHasBinding_ThenUsesValidMedicationRequestStatusCode()
    {
        // Arrange & Act
        var medRequest = _faker.Generate("MedicationRequest");

        // Assert
        var status = medRequest.MutableNode["status"]?.GetValue<string>();
        if (status is not null)
        {
            status.Should().BeOneOf(
                "active", "on-hold", "cancelled", "completed",
                "entered-in-error", "stopped", "draft", "unknown",
                "MedicationRequest.status should use medicationrequest-status value set");
        }
    }

    [Fact]
    public void GenerateMedicationRequest_WhenIntentHasBinding_ThenUsesValidMedicationRequestIntentCode()
    {
        // Arrange & Act
        var medRequest = _faker.Generate("MedicationRequest");

        // Assert
        var intent = medRequest.MutableNode["intent"]?.GetValue<string>();
        if (intent is not null)
        {
            intent.Should().BeOneOf(
                "proposal", "plan", "order", "original-order",
                "reflex-order", "filler-order", "instance-order", "option",
                "MedicationRequest.intent should use medicationrequest-intent value set");
        }
    }

    [Fact]
    public void GenerateDiagnosticReport_WhenStatusHasBinding_ThenUsesValidDiagnosticReportStatusCode()
    {
        // Arrange & Act
        var report = _faker.Generate("DiagnosticReport");

        // Assert
        var status = report.MutableNode["status"]?.GetValue<string>();
        if (status is not null)
        {
            status.Should().BeOneOf(
                "registered", "partial", "preliminary", "final", "amended",
                "corrected", "appended", "cancelled", "entered-in-error", "unknown",
                "DiagnosticReport.status should use diagnostic-report-status value set");
        }
    }

    [Fact]
    public void GenerateImmunization_WhenStatusHasBinding_ThenUsesValidImmunizationStatusCode()
    {
        // Arrange & Act
        var immunization = _faker.Generate("Immunization");

        // Assert
        var status = immunization.MutableNode["status"]?.GetValue<string>();
        if (status is not null)
        {
            status.Should().BeOneOf("completed", "entered-in-error", "not-done",
                "Immunization.status should use immunization-status value set");
        }
    }

    #endregion

    #region Multiple Generation Consistency Tests

    [Fact]
    public void GenerateMultiplePatients_WhenGenderHasBinding_ThenAllUsesValidGenderCodes()
    {
        // Arrange
        var generatedGenders = new List<string>();

        // Act
        for (int i = 0; i < 50; i++)
        {
            var patient = _faker.Generate("Patient");
            var gender = patient.MutableNode["gender"]?.GetValue<string>();
            if (gender is not null)
            {
                generatedGenders.Add(gender);
            }
        }

        // Assert
        generatedGenders.Should().NotBeEmpty();
        generatedGenders.Should().AllSatisfy(g =>
            g.Should().BeOneOf("male", "female", "other", "unknown"));
    }

    [Fact]
    public void GenerateMultipleObservations_WhenStatusHasBinding_ThenAllUsesValidStatusCodes()
    {
        // Arrange
        var generatedStatuses = new List<string>();

        // Act
        for (int i = 0; i < 50; i++)
        {
            var observation = _faker.Generate("Observation");
            var status = observation.MutableNode["status"]?.GetValue<string>();
            if (status is not null)
            {
                generatedStatuses.Add(status);
            }
        }

        // Assert
        generatedStatuses.Should().NotBeEmpty();
        generatedStatuses.Should().AllSatisfy(s =>
            s.Should().BeOneOf("registered", "preliminary", "final", "amended",
                "corrected", "cancelled", "entered-in-error", "unknown"));
    }

    #endregion
}
