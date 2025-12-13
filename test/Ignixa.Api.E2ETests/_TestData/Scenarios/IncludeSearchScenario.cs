// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.Api.E2ETests._TestData.Scenarios;

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

        var parentLocation = LocationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithName("Test Location")
            .WithStatus("active")
            .WithManagingOrganization(data.Organization.Id!)
            .Build();

        data.Location = parentLocation;

        var childLocation = LocationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithName("Test Location")
            .WithStatus("active")
            .WithPartOf(parentLocation.Id!)
            .Build();

        data.ChildLocation = childLocation;

        // === Step 3: Create Practitioner ===

        var practitioner = PractitionerBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithName("Alice", "Anderson")
            .Build();

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
        data.Observation1 = ObservationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithStatus("final")
            .WithCode(loincCode.Code, loincCode.System, loincCode.Display)
            .WithSubject(data.Patient1.Id!)
            .WithQuantityValue(6.5m, "%")
            .WithPractitionerPerformer(data.Practitioner.Id!)
            .Build();

        // Observation2: subject=Patient1, performer=Organization
        data.Observation2 = ObservationBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithStatus("final")
            .WithCode(snomedCode.Code, snomedCode.System, snomedCode.Display)
            .WithSubject(data.Patient1.Id!)
            .WithQuantityValue(145m, "mg/dL")
            .WithOrganizationPerformer(data.Organization.Id!)
            .Build();

        // === Step 6: Create DiagnosticReport ===

        data.DiagnosticReport = DiagnosticReportBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithStatus("final")
            .WithCode(snomedCode.Code, snomedCode.System, snomedCode.Display)
            .WithSubject(data.Patient1.Id!)
            .WithResults(data.Observation1.Id!, data.Observation2.Id!)
            .Build();

        // === Step 7: Create Group ===

        var group = GroupBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithType("person")
            .WithActual(true)
            .WithMembers(data.Patient1.Id!, data.Patient2.Id!)
            .Build();

        data.Group = group;

        // === Step 8: Create CareTeam ===

        var careTeam = CareTeamBuilder.Create(schemaProvider)
            .WithTag(tag)
            .WithStatus("active")
            .WithPatientParticipant(data.Patient1.Id!)
            .WithOrganizationParticipant(data.Organization.Id!)
            .WithPractitionerParticipant(data.Practitioner.Id!)
            .Build();

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
