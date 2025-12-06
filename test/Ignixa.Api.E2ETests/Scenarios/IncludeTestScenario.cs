// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.Api.E2ETests.Scenarios;

/// <summary>
/// Provides test scenarios specifically designed for testing FHIR _include and _revinclude search functionality.
/// These scenarios create resources with various reference types to enable comprehensive include/revinclude tests.
/// </summary>
/// <remarks>
/// This scenario uses resource builders directly to create standalone resources and complex
/// multi-patient/multi-organization graphs. ScenarioBuilder is NOT used here because it's
/// patient-centric (one scenario = one patient + their related resources).
///
/// Key test patterns covered:
/// <list type="bullet">
///   <item><description>Basic _include: DiagnosticReport?_include=DiagnosticReport:patient:Patient</description></item>
///   <item><description>Basic _revinclude: Patient?_revinclude=Observation:patient</description></item>
///   <item><description>Multi-type references: Observation:performer (Practitioner/Organization)</description></item>
///   <item><description>Self-references: Organization.partOf, Location.partOf</description></item>
///   <item><description>Array references: Group.member, CareTeam.participant</description></item>
/// </list>
/// </remarks>
public static class IncludeTestScenario
{
    /// <summary>
    /// Creates a comprehensive test scenario for _include and _revinclude functionality.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag to isolate test data (typically a GUID).</param>
    /// <returns>A <see cref="IncludeTestData"/> containing all resources and their IDs.</returns>
    /// <remarks>
    /// Generated Resources:
    /// <list type="bullet">
    ///   <item><description>1 Organization</description></item>
    ///   <item><description>2 Locations (parent and child with partOf reference, parent linked to organization)</description></item>
    ///   <item><description>1 Practitioner (for Patient.generalPractitioner and Observation.performer)</description></item>
    ///   <item><description>2 Patients (one with organization and practitioner references)</description></item>
    ///   <item><description>2 Observations (with different performer types)</description></item>
    ///   <item><description>1 DiagnosticReport (with result references)</description></item>
    ///   <item><description>1 Group (containing both patients)</description></item>
    ///   <item><description>1 CareTeam (with mixed participant types)</description></item>
    /// </list>
    /// </remarks>
    public static IncludeTestData GetIncludeSearchScenario(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var faker = new SchemaBasedFhirResourceFaker(schemaProvider).WithTag(tag);

        var data = new IncludeTestData
        {
            Tag = tag,
            AllResources = []
        };

        // Create unique codes for observations
        var loincCode = new FhirCode(FhirCode.Systems.Loinc, "4548-4", "Hemoglobin A1c");
        var snomedCode = new FhirCode(FhirCode.Systems.SnomedCt, "429858000", "SNOMED Observation");

        // === Step 1: Create Organizations (parent and child) ===
        // Note: Using OrganizationBuilder directly instead of ScenarioBuilder because this
        // scenario needs multiple organizations with partOf relationships, not patient-centric data.

        var parentOrg = OrganizationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithName("Parent Healthcare Organization")
            .WithType("prov", display: "Healthcare Provider")
            .WithAddress("123 Main St", "Seattle", "WA", "98101")
            .Build();

        data.Organization = parentOrg;

        // === Step 2: Create Locations (parent and child) ===

        var parentLocation = CreateLocation(faker, data.Organization.Id);
        data.Location = parentLocation;

        var childLocation = CreateLocation(faker, partOfLocationId: parentLocation.Id);
        data.ChildLocation = childLocation;

        // === Step 3: Create Practitioner ===
        // Note: No PractitionerBuilder exists yet, so using faker with manual configuration.

        var practitioner = faker.Generate("Practitioner");
        practitioner.MutableNode["name"] = new JsonArray
        {
            new JsonObject
            {
                ["family"] = "Anderson",
                ["given"] = new JsonArray { "Alice" }
            }
        };

        data.Practitioner = practitioner;

        // === Step 4: Create Patients ===
        // Note: Using PatientBuilder directly instead of ScenarioBuilder because this scenario
        // needs multiple unrelated patients. ScenarioBuilder is patient-centric (one scenario = one patient).

        var patient1 = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithAge(35)
            .WithGender(g => g.Female)
            .WithFamilyName("Smith")
            .WithGivenName("Jane")
            .WithManagingOrganization(data.Organization.Id!)
            .WithGeneralPractitioner(data.Practitioner.Id!)
            .Build();

        data.Patient1 = patient1;

        var patient2 = new PatientBuilder(schemaProvider)
            .WithTag(tag)
            .WithAge(42)
            .WithGender(g => g.Male)
            .WithFamilyName("Jones")
            .WithGivenName("Bob")
            .Build();

        data.Patient2 = patient2;

        // === Step 5: Create Observations ===

        // Observation1: subject=Patient1, performer=Practitioner
        data.Observation1 = CreateObservation(
            faker,
            data.Patient1.Id!,
            loincCode,
            6.5m,
            "%",
            practitionerId: data.Practitioner.Id);

        // Observation2: subject=Patient1, performer=Organization
        data.Observation2 = CreateObservation(
            faker,
            data.Patient1.Id!,
            snomedCode,
            145m,
            "mg/dL",
            organizationId: data.Organization.Id);

        // === Step 6: Create DiagnosticReport ===

        data.DiagnosticReport = CreateDiagnosticReport(
            faker,
            data.Patient1.Id!,
            snomedCode,
            data.Observation1.Id!,
            data.Observation2.Id!);

        // === Step 7: Create Group ===

        var group = CreateGroup(faker, data.Patient1.Id!, data.Patient2.Id!);
        data.Group = group;

        // === Step 8: Create CareTeam ===

        var careTeam = CreateCareTeam(faker,
            [data.Patient1.Id!],
            data.Organization.Id!,
            data.Practitioner.Id!);
        data.CareTeam = careTeam;

        // === Populate AllResources in dependency order ===

        // Organizations first
        data.AllResources.Add(data.Organization);

        // Locations (depend on organizations)
        data.AllResources.Add(data.Location);
        data.AllResources.Add(data.ChildLocation);

        // Practitioners
        data.AllResources.Add(data.Practitioner);

        // Patients (depend on organizations and practitioners)
        data.AllResources.Add(data.Patient1);
        data.AllResources.Add(data.Patient2);

        // Observations (depend on patients)
        data.AllResources.Add(data.Observation1);
        data.AllResources.Add(data.Observation2);

        // DiagnosticReport (depends on patients and observations)
        data.AllResources.Add(data.DiagnosticReport);

        // Group (depends on patients)
        data.AllResources.Add(data.Group);

        // CareTeam (depends on patients, organization, practitioner)
        data.AllResources.Add(data.CareTeam);

        return data;
    }

    #region Private Helper Methods

    private static JsonObject CreateReferenceJson(string resourceType, string id)
    {
        return new JsonObject
        {
            ["reference"] = $"{resourceType}/{id}"
        };
    }

    private static JsonObject CreateCodeableConceptJson(FhirCode code)
    {
        return new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = code.System,
                    ["code"] = code.Code,
                    ["display"] = code.Display
                }
            }
        };
    }

    /// <summary>
    /// Creates a Location resource with optional organization and partOf references.
    /// </summary>
    private static ResourceJsonNode CreateLocation(
        SchemaBasedFhirResourceFaker faker,
        string? managingOrganizationId = null,
        string? partOfLocationId = null)
    {
        var location = faker.Generate("Location");
        location.MutableNode["status"] = "active";
        location.MutableNode["name"] = "Test Location";

        if (managingOrganizationId is not null)
        {
            location.MutableNode["managingOrganization"] = CreateReferenceJson("Organization", managingOrganizationId);
        }

        if (partOfLocationId is not null)
        {
            location.MutableNode["partOf"] = CreateReferenceJson("Location", partOfLocationId);
        }

        return location;
    }

    /// <summary>
    /// Creates an Observation resource with subject and optional performer references.
    /// </summary>
    private static ResourceJsonNode CreateObservation(
        SchemaBasedFhirResourceFaker faker,
        string patientId,
        FhirCode code,
        decimal value,
        string unit,
        string? practitionerId = null,
        string? organizationId = null)
    {
        var observation = faker.Generate("Observation");
        observation.MutableNode["status"] = "final";
        observation.MutableNode["code"] = CreateCodeableConceptJson(code);
        observation.MutableNode["subject"] = CreateReferenceJson("Patient", patientId);
        observation.MutableNode["valueQuantity"] = new JsonObject
        {
            ["value"] = value,
            ["unit"] = unit
        };

        if (practitionerId is not null || organizationId is not null)
        {
            var performers = new JsonArray();
            if (practitionerId is not null)
            {
                performers.Add(CreateReferenceJson("Practitioner", practitionerId));
            }
            if (organizationId is not null)
            {
                performers.Add(CreateReferenceJson("Organization", organizationId));
            }
            observation.MutableNode["performer"] = performers;
        }

        return observation;
    }

    /// <summary>
    /// Creates a DiagnosticReport resource with subject and result references.
    /// </summary>
    private static ResourceJsonNode CreateDiagnosticReport(
        SchemaBasedFhirResourceFaker faker,
        string patientId,
        FhirCode code,
        params string[] observationIds)
    {
        var report = faker.Generate("DiagnosticReport");
        report.MutableNode["status"] = "final";
        report.MutableNode["code"] = CreateCodeableConceptJson(code);
        report.MutableNode["subject"] = CreateReferenceJson("Patient", patientId);

        if (observationIds.Length > 0)
        {
            var results = new JsonArray();
            foreach (var obsId in observationIds)
            {
                results.Add(CreateReferenceJson("Observation", obsId));
            }
            report.MutableNode["result"] = results;
        }

        return report;
    }

    /// <summary>
    /// Creates a Group resource with member references.
    /// </summary>
    private static ResourceJsonNode CreateGroup(SchemaBasedFhirResourceFaker faker, params string[] patientIds)
    {
        var group = faker.Generate("Group");
        group.MutableNode["type"] = "person";
        group.MutableNode["actual"] = true;
        group.MutableNode["member"] = CreateGroupMemberArray(patientIds);

        return group;
    }

    /// <summary>
    /// Creates a Group.member array with Patient references.
    /// </summary>
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

    /// <summary>
    /// Creates a CareTeam resource with participant references.
    /// </summary>
    private static ResourceJsonNode CreateCareTeam(
        SchemaBasedFhirResourceFaker faker,
        string[] patientIds,
        string? organizationId = null,
        string? practitionerId = null)
    {
        var careTeam = faker.Generate("CareTeam");
        careTeam.MutableNode["status"] = "active";
        careTeam.MutableNode["participant"] = CreateCareTeamParticipantArray(patientIds, organizationId, practitionerId);

        return careTeam;
    }

    /// <summary>
    /// Creates a CareTeam.participant array with member references.
    /// </summary>
    private static JsonArray CreateCareTeamParticipantArray(
        string[] patientIds,
        string? organizationId,
        string? practitionerId)
    {
        var participants = new JsonArray();

        foreach (var patientId in patientIds)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Patient", patientId)
            });
        }

        if (organizationId is not null)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Organization", organizationId)
            });
        }

        if (practitionerId is not null)
        {
            participants.Add(new JsonObject
            {
                ["member"] = CreateReferenceJson("Practitioner", practitionerId)
            });
        }

        return participants;
    }

    #endregion
}

/// <summary>
/// Contains all test data and resource references for include/revinclude search tests.
/// </summary>
public sealed class IncludeTestData
{
    /// <summary>
    /// Gets the test isolation tag applied to all resources.
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Gets all resources in dependency order for batch/transaction creation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> AllResources { get; init; }

    // === Organizations ===

    /// <summary>
    /// Gets the parent organization.
    /// </summary>
    public ResourceJsonNode Organization { get; set; } = null!;

    // === Locations ===

    /// <summary>
    /// Gets the parent location (linked to parent organization).
    /// </summary>
    public ResourceJsonNode Location { get; set; } = null!;

    /// <summary>
    /// Gets the child location (with partOf reference to parent location).
    /// </summary>
    public ResourceJsonNode ChildLocation { get; set; } = null!;

    // === Practitioners ===

    /// <summary>
    /// Gets the practitioner (for generalPractitioner and performer references).
    /// </summary>
    public ResourceJsonNode Practitioner { get; set; } = null!;

    // === Patients ===

    /// <summary>
    /// Gets Patient1 (with managingOrganization and generalPractitioner references).
    /// </summary>
    public ResourceJsonNode Patient1 { get; set; } = null!;

    /// <summary>
    /// Gets Patient2 (basic patient for Group testing).
    /// </summary>
    public ResourceJsonNode Patient2 { get; set; } = null!;

    // === Observations ===

    /// <summary>
    /// Gets Observation1 (subject=Patient1, performer=Practitioner).
    /// </summary>
    public ResourceJsonNode Observation1 { get; set; } = null!;

    /// <summary>
    /// Gets Observation2 (subject=Patient1, performer=Organization).
    /// </summary>
    public ResourceJsonNode Observation2 { get; set; } = null!;

    // === DiagnosticReports ===

    /// <summary>
    /// Gets the DiagnosticReport (subject=Patient1, result=[Observation1, Observation2]).
    /// </summary>
    public ResourceJsonNode DiagnosticReport { get; set; } = null!;

    // === Group ===

    /// <summary>
    /// Gets the Group (containing Patient1 and Patient2 as members).
    /// </summary>
    public ResourceJsonNode Group { get; set; } = null!;

    // === CareTeam ===

    /// <summary>
    /// Gets the CareTeam (participants: Patient1, Organization, Practitioner).
    /// </summary>
    public ResourceJsonNode CareTeam { get; set; } = null!;
}
