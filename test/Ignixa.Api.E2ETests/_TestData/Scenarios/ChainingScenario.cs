// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.Api.E2ETests._TestData.Scenarios;

/// <summary>
/// Provides test scenarios specifically designed for testing FHIR chained and reverse-chained (_has) search functionality.
/// These scenarios create multiple patients with related resources to enable comprehensive chaining search tests.
/// </summary>
/// <remarks>
/// Unlike <see cref="ChainedSearchTestScenario"/> which uses the single-patient ScenarioBuilder pattern,
/// this class creates multi-patient scenarios needed for testing search result filtering across different patients.
///
/// Key search patterns tested:
/// <list type="bullet">
///   <item><description>Forward chains: DiagnosticReport?subject:Patient.name=Smith</description></item>
///   <item><description>Nested chains: DiagnosticReport?result.subject:Patient.organization.address-city=Boston</description></item>
///   <item><description>Reverse chains (_has): Patient?_has:Observation:patient:code=SNOMED-CODE</description></item>
///   <item><description>Combined chains: DiagnosticReport?code=X&amp;patient:Patient._has:Group:member:_tag=Y</description></item>
/// </list>
/// </remarks>
public static class ChainingTestScenario
{
    /// <summary>
    /// Creates a comprehensive test scenario for chained search functionality.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag to isolate test data (typically a GUID).</param>
    /// <returns>A <see cref="ChainingTestData"/> containing all resources and searchable values.</returns>
    /// <remarks>
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>1 Organization (with unique city and identifier for chaining)</description></item>
    ///   <item><description>1 Location (Seattle)</description></item>
    ///   <item><description>3 Patients: Adams (female, no org), Smith (male, linked to org), Truman (male, no org)</description></item>
    ///   <item><description>2 Devices (for Observation.subject Device reference tests)</description></item>
    ///   <item><description>7 Observations (LOINC and SNOMED codes for patients and devices)</description></item>
    ///   <item><description>4 DiagnosticReports (linked to observations)</description></item>
    ///   <item><description>1 CareTeam (linked to Adams)</description></item>
    ///   <item><description>1 Group (containing Adams, Smith, Truman)</description></item>
    /// </list>
    ///
    /// Key searchable values returned in ChainingTestData:
    /// <list type="bullet">
    ///   <item><description>SmithPatientGivenName: Unique given name for Smith (for Patient.name chain)</description></item>
    ///   <item><description>TrumanPatientGivenName: Unique given name for Truman (for OR searches)</description></item>
    ///   <item><description>SnomedCode: Unique SNOMED code (for Observation.code chain)</description></item>
    ///   <item><description>OrganizationCity: Unique city (for Organization.address-city chain)</description></item>
    ///   <item><description>OrganizationIdentifier: Unique identifier (for Organization.identifier chain)</description></item>
    /// </list>
    /// </remarks>
    public static ChainingTestData GetChainingSearchScenario(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var faker = new SchemaBasedFhirResourceFaker(schemaProvider).WithTag(tag);

        // Generate unique searchable values
        var data = new ChainingTestData
        {
            Tag = tag,
            SmithPatientGivenName = Guid.NewGuid().ToString(),
            TrumanPatientGivenName = Guid.NewGuid().ToString(),
            SnomedCode = Guid.NewGuid().ToString(),
            OrganizationCity = Guid.NewGuid().ToString(),
            OrganizationIdentifier = Guid.NewGuid().ToString()
        };

        // Create code objects for observations
        var snomedCode = new FhirCode(FhirCode.Systems.SnomedCt, data.SnomedCode, "SNOMED Observation");
        var loincCode = new FhirCode(FhirCode.Systems.Loinc, FhirCode.Observations.HemoglobinA1c.Code, "LOINC Observation");

        // === Scenario 1: Adams (Female, No Organization, LOINC only) ===
        var adamsScenario = new ScenarioBuilder(schemaProvider)
            .WithName("Adams Patient Journey")
            .WithTag(tag)
            .WithResolvedReferences()
            .WithPatient(age: 32, gender: "female", familyName: "Adams")
            .AddEncounter("Annual checkup - Adams")
            .AddState(new ObservationState
            {
                StateId = "adams_loinc_obs",
                Code = loincCode,
                Value = 105m,
                Unit = "mg/dL"
            })
            .AddCareTeam("Adams Care Team")
            .Build();

        // Extract Adams resources
        data.AdamsPatient = adamsScenario.Patient!;
        data.AdamsLoincObservation = adamsScenario.Observations[0];
        data.AdamsCareTeam = adamsScenario.CareTeams[0];

        // === Scenario 2: Smith (Male, WITH Organization, SNOMED + LOINC) ===

        // First create organization standalone to get its ID
        var organizationState = new OrganizationState
        {
            Name = "Organization_MainClinic",
            OrganizationName = "Main Clinic",
            CustomIdentifiers = [("http://test-system", data.OrganizationIdentifier)],
            Type = new FhirCode("http://terminology.hl7.org/CodeSystem/organization-type", "practice", "Practice"),
            Address = new OrganizationAddress(
                Line: "100 Main St",
                City: data.OrganizationCity,  // Unique city for search
                State: "WA",
                PostalCode: "98101"
            )
        };

        var orgScenario = new ScenarioBuilder(schemaProvider)
            .WithTag(tag)
            .WithResolvedReferences()
            .AddOrganization(organizationState)
            .Build();

        data.Organization = orgScenario.Organizations[0];

        // Now create Smith patient with organization link
        var smithScenario = new ScenarioBuilder(schemaProvider)
            .WithName("Smith Patient Journey")
            .WithTag(tag)
            .WithResolvedReferences()
            .WithPatient(p => p
                .WithAge(45)
                .WithGender(g => g.Male)
                .WithGivenName(data.SmithPatientGivenName)
                .WithFamilyName("Smith")
                .WithManagingOrganization(data.Organization.Id!))
            .AddEncounter("Diabetes follow-up - Smith")

            // Create observations with StateIds
            .AddState(new ObservationState
            {
                StateId = "smith_snomed_obs",
                Code = snomedCode,
                Value = 145m,
                Unit = "mg/dL"
            })
            .AddState(new ObservationState
            {
                StateId = "smith_loinc_obs",
                Code = loincCode,
                Value = 7.2m,
                Unit = "%"
            })

            // DiagnosticReports reference observations by StateId
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = new FhirCode("http://snomed.info/sct", data.SnomedCode, "SNOMED Report"),
                ReferencedObservationStateIds = ["smith_snomed_obs"]
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = new FhirCode("http://loinc.org", "4548-4", "LOINC Report"),
                ReferencedObservationStateIds = ["smith_loinc_obs"]
            })

            .Build();

        // Extract Smith resources
        data.SmithPatient = smithScenario.Patient!;
        data.SmithSnomedObservation = smithScenario.Observations[0];
        data.SmithLoincObservation = smithScenario.Observations[1];
        data.SmithSnomedDiagnosticReport = smithScenario.DiagnosticReports[0];
        data.SmithLoincDiagnosticReport = smithScenario.DiagnosticReports[1];

        // === Scenario 3: Truman (Male, No Organization, SNOMED + LOINC) ===
        var trumanScenario = new ScenarioBuilder(schemaProvider)
            .WithName("Truman Patient Journey")
            .WithTag(tag)
            .WithResolvedReferences()
            .WithPatient(age: 48, gender: "male",
                givenName: data.TrumanPatientGivenName,
                familyName: "Truman")
            .AddEncounter("Annual physical - Truman")
            .AddState(new ObservationState
            {
                StateId = "truman_snomed_obs",
                Code = snomedCode,
                Value = 132m,
                Unit = "mg/dL"
            })
            .AddState(new ObservationState
            {
                StateId = "truman_loinc_obs",
                Code = loincCode,
                Value = 6.8m,
                Unit = "%"
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = new FhirCode("http://snomed.info/sct", data.SnomedCode, "SNOMED Report"),
                ReferencedObservationStateIds = ["truman_snomed_obs"]
            })
            .AddDiagnosticReport(new DiagnosticReportState
            {
                Code = new FhirCode("http://loinc.org", "4548-4", "LOINC Report"),
                ReferencedObservationStateIds = ["truman_loinc_obs"]
            })
            .Build();

        // Extract Truman resources
        data.TrumanPatient = trumanScenario.Patient!;
        data.TrumanSnomedObservation = trumanScenario.Observations[0];
        data.TrumanLoincObservation = trumanScenario.Observations[1];
        data.TrumanSnomedDiagnosticReport = trumanScenario.DiagnosticReports[0];
        data.TrumanLoincDiagnosticReport = trumanScenario.DiagnosticReports[1];

        // === Cross-Patient Resources (Location, Devices, Group) ===

        // Create Location
        var location = faker.Generate("Location");
        location.MutableNode["address"] = new JsonObject { ["city"] = "Seattle" };
        data.Location = location;

        // Create Devices
        data.DeviceLoincSubject = faker.Generate("Device");
        data.DeviceSnomedSubject = faker.Generate("Device");

        // Create Device observations (not part of scenarios)
        var loincCodeJson = CreateCodeableConceptJson(loincCode.System, loincCode.Code);
        var snomedCodeJson = CreateCodeableConceptJson(snomedCode.System, snomedCode.Code);
        data.DeviceLoincObservation = CreateObservation(faker, data.DeviceLoincSubject, loincCodeJson, "Device");
        data.DeviceSnomedObservation = CreateObservation(faker, data.DeviceSnomedSubject, snomedCodeJson, "Device");

        // Create Group containing all three patients
        var group = faker.Generate("Group");
        group.MutableNode["type"] = "person";
        group.MutableNode["actual"] = true;
        group.MutableNode["member"] = CreateGroupMemberArray(
            data.AdamsPatient.Id!,
            data.SmithPatient.Id!,
            data.TrumanPatient.Id!);
        data.PatientGroup = group;

        // Populate AllResources in dependency order for transaction bundle
        data.AllResources.AddRange(orgScenario.AllResources);  // Organization must come first
        data.AllResources.AddRange(adamsScenario.AllResources);
        data.AllResources.AddRange(smithScenario.AllResources);
        data.AllResources.AddRange(trumanScenario.AllResources);
        data.AllResources.Add(data.Location);
        data.AllResources.Add(data.DeviceLoincSubject);
        data.AllResources.Add(data.DeviceSnomedSubject);
        data.AllResources.Add(data.DeviceLoincObservation);
        data.AllResources.Add(data.DeviceSnomedObservation);
        data.AllResources.Add(data.PatientGroup);

        return data;
    }

    /// <summary>
    /// Creates resources for testing deleted resource handling in reverse chain searches.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag to isolate test data.</param>
    /// <returns>A tuple containing the patient to be deleted and its associated CareTeam.</returns>
    /// <remarks>
    /// This creates a Patient with an associated CareTeam, where the patient is intended
    /// to be deleted after creation. Used to verify that reverse chain searches with
    /// _summary=count correctly exclude deleted resources.
    /// </remarks>
    public static (ResourceJsonNode Patient, ResourceJsonNode CareTeam) GetDeletedPatientCareTeamScenario(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var scenario = new ScenarioBuilder(schemaProvider)
            .WithTag(tag)
            .WithResolvedReferences()
            .WithPatient(gender: "male", givenName: "Delete", familyName: "Delete")
            .AddCareTeam("Delete Care Team")
            .Build();

        return (scenario.Patient!, scenario.CareTeams[0]);
    }

    #region Private Helper Methods

    private static JsonObject CreateCodeableConceptJson(string system, string code)
    {
        return new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = system,
                    ["code"] = code
                }
            }
        };
    }

    private static JsonObject CreateReferenceJson(string resourceType, string id)
    {
        return new JsonObject
        {
            ["reference"] = $"{resourceType}/{id}"
        };
    }

    private static JsonArray CreateGroupMemberArray(params string[] patientIds)
    {
        var members = new JsonArray();
        foreach (var id in patientIds)
        {
            members.Add(new JsonObject
            {
                ["entity"] = CreateReferenceJson("Patient", id)
            });
        }
        return members;
    }

    private static ResourceJsonNode CreateObservation(
        SchemaBasedFhirResourceFaker faker,
        ResourceJsonNode subject,
        JsonObject code,
        string subjectType)
    {
        var observation = faker.Generate("Observation");
        observation.MutableNode["status"] = "final";
        observation.MutableNode["code"] = code.DeepClone();
        observation.MutableNode["subject"] = CreateReferenceJson(subjectType, subject.Id!);
        return observation;
    }

    private static ResourceJsonNode CreateDiagnosticReport(
        SchemaBasedFhirResourceFaker faker,
        ResourceJsonNode patient,
        ResourceJsonNode observation,
        JsonObject code)
    {
        var report = faker.Generate("DiagnosticReport");
        report.MutableNode["status"] = "final";
        report.MutableNode["code"] = code.DeepClone();
        report.MutableNode["subject"] = CreateReferenceJson("Patient", patient.Id!);
        report.MutableNode["result"] = new JsonArray
        {
            CreateReferenceJson("Observation", observation.Id!)
        };
        return report;
    }

    #endregion
}

/// <summary>
/// Contains all test data and searchable values for chaining search tests.
/// </summary>
public sealed class ChainingTestData
{
    /// <summary>
    /// Gets the test isolation tag applied to all resources.
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Gets the unique given name for the Smith patient (for Patient.name chain searches).
    /// </summary>
    public required string SmithPatientGivenName { get; init; }

    /// <summary>
    /// Gets the unique given name for the Truman patient (for OR condition searches).
    /// </summary>
    public required string TrumanPatientGivenName { get; init; }

    /// <summary>
    /// Gets the unique SNOMED code used for observations and diagnostic reports.
    /// </summary>
    public required string SnomedCode { get; init; }

    /// <summary>
    /// Gets the unique city for the organization (for Organization.address-city chain).
    /// </summary>
    public required string OrganizationCity { get; init; }

    /// <summary>
    /// Gets the unique identifier for the organization.
    /// </summary>
    public required string OrganizationIdentifier { get; init; }

    // === Organizations and Locations ===

    /// <summary>
    /// Gets the test organization with unique city and identifier.
    /// </summary>
    public ResourceJsonNode Organization { get; set; } = null!;

    /// <summary>
    /// Gets the test location (Seattle).
    /// </summary>
    public ResourceJsonNode Location { get; set; } = null!;

    // === Patients ===

    /// <summary>
    /// Gets the Adams patient (female, no organization link).
    /// </summary>
    public ResourceJsonNode AdamsPatient { get; set; } = null!;

    /// <summary>
    /// Gets the Smith patient (male, linked to organization).
    /// </summary>
    public ResourceJsonNode SmithPatient { get; set; } = null!;

    /// <summary>
    /// Gets the Truman patient (male, no organization link).
    /// </summary>
    public ResourceJsonNode TrumanPatient { get; set; } = null!;

    // === Devices ===

    /// <summary>
    /// Gets the device used as subject for LOINC observations.
    /// </summary>
    public ResourceJsonNode DeviceLoincSubject { get; set; } = null!;

    /// <summary>
    /// Gets the device used as subject for SNOMED observations.
    /// </summary>
    public ResourceJsonNode DeviceSnomedSubject { get; set; } = null!;

    // === Observations ===

    /// <summary>
    /// Gets the Adams patient's LOINC observation.
    /// </summary>
    public ResourceJsonNode AdamsLoincObservation { get; set; } = null!;

    /// <summary>
    /// Gets the Smith patient's LOINC observation.
    /// </summary>
    public ResourceJsonNode SmithLoincObservation { get; set; } = null!;

    /// <summary>
    /// Gets the Smith patient's SNOMED observation.
    /// </summary>
    public ResourceJsonNode SmithSnomedObservation { get; set; } = null!;

    /// <summary>
    /// Gets the Truman patient's LOINC observation.
    /// </summary>
    public ResourceJsonNode TrumanLoincObservation { get; set; } = null!;

    /// <summary>
    /// Gets the Truman patient's SNOMED observation.
    /// </summary>
    public ResourceJsonNode TrumanSnomedObservation { get; set; } = null!;

    /// <summary>
    /// Gets the device's LOINC observation.
    /// </summary>
    public ResourceJsonNode DeviceLoincObservation { get; set; } = null!;

    /// <summary>
    /// Gets the device's SNOMED observation.
    /// </summary>
    public ResourceJsonNode DeviceSnomedObservation { get; set; } = null!;

    // === Diagnostic Reports ===

    /// <summary>
    /// Gets the Smith patient's SNOMED diagnostic report.
    /// </summary>
    public ResourceJsonNode SmithSnomedDiagnosticReport { get; set; } = null!;

    /// <summary>
    /// Gets the Truman patient's SNOMED diagnostic report.
    /// </summary>
    public ResourceJsonNode TrumanSnomedDiagnosticReport { get; set; } = null!;

    /// <summary>
    /// Gets the Smith patient's LOINC diagnostic report.
    /// </summary>
    public ResourceJsonNode SmithLoincDiagnosticReport { get; set; } = null!;

    /// <summary>
    /// Gets the Truman patient's LOINC diagnostic report.
    /// </summary>
    public ResourceJsonNode TrumanLoincDiagnosticReport { get; set; } = null!;

    // === CareTeam and Group ===

    /// <summary>
    /// Gets the CareTeam linked to the Adams patient.
    /// </summary>
    public ResourceJsonNode AdamsCareTeam { get; set; } = null!;

    /// <summary>
    /// Gets the Group containing Adams, Smith, and Truman as members.
    /// </summary>
    public ResourceJsonNode PatientGroup { get; set; } = null!;

    /// <summary>
    /// Gets all resources in dependency order for batch/transaction creation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> AllResources { get; } = [];
}
