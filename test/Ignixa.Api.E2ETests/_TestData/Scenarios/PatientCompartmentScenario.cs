// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Abstractions;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.Api.E2ETests._TestData.Scenarios;

/// <summary>
/// Provides test scenarios for Patient compartment testing.
/// Creates a patient with a complete set of linked resources covering all compartment types.
/// </summary>
/// <remarks>
/// <para>
/// Patient compartment scenarios are designed to test FHIR compartment search functionality,
/// which returns all resources linked to a specific patient via defined search parameters.
/// </para>
/// <para>
/// This scenario uses ScenarioBuilder to create a patient-centric resource graph.
/// Unlike IncludeTestScenario (which uses builders directly for multi-patient scenarios),
/// this is patient-focused and leverages ScenarioBuilder's state management.
/// </para>
/// <para>
/// FHIR Specification: http://hl7.org/fhir/compartmentdefinition.html
/// </para>
/// </remarks>
public static class PatientCompartmentScenario
{
    /// <summary>
    /// Creates a comprehensive patient compartment with multiple resource types.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag to isolate test data (typically a GUID).</param>
    /// <returns>A <see cref="PatientCompartmentData"/> containing all compartment resources.</returns>
    /// <remarks>
    /// Generated Resources (all linked to the patient):
    /// <list type="bullet">
    ///   <item><description>1 Patient (compartment owner)</description></item>
    ///   <item><description>1 Encounter (ambulatory visit)</description></item>
    ///   <item><description>3 Observations (weight, height, blood pressure)</description></item>
    ///   <item><description>1 Condition (hypertension)</description></item>
    ///   <item><description>1 MedicationRequest (for hypertension treatment)</description></item>
    ///   <item><description>1 DiagnosticReport (comprehensive metabolic panel)</description></item>
    ///   <item><description>1 Immunization (influenza vaccine)</description></item>
    ///   <item><description>1 AllergyIntolerance (penicillin allergy)</description></item>
    ///   <item><description>1 Procedure (colonoscopy)</description></item>
    ///   <item><description>1 ServiceRequest (lipid panel order)</description></item>
    ///   <item><description>1 Goal (blood pressure control)</description></item>
    ///   <item><description>1 CarePlan (hypertension management)</description></item>
    ///   <item><description>1 CareTeam (care coordination team)</description></item>
    ///   <item><description>1 Coverage (insurance)</description></item>
    ///   <item><description>1 Practitioner (primary care physician)</description></item>
    ///   <item><description>1 Organization (managing organization)</description></item>
    /// </list>
    /// </remarks>
    public static PatientCompartmentData CreateCompartmentScenario(
        this IFhirSchemaProvider schemaProvider,
        string tag)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        // Create comprehensive patient scenario with common compartment resources
        var scenario = new ScenarioBuilder(schemaProvider)
            .WithName("Patient Compartment Test Scenario")
            .WithDescription("Complete patient compartment with multiple resource types for compartment search testing")
            .WithTag(tag)
            .WithResolvedReferences()

            // Create managing organization (referenced by patient)
            .AddHospital("Compartment Test Hospital")

            // Create primary care practitioner (referenced by patient)
            .AddFamilyPractitioner()

            // Create patient from Seattle with realistic demographics
            .WithSeattlePatient(p => p
                .WithAge(45)
                .WithRealisticBMI()
                .WithFamilyName("CompartmentPatient"))

            // Add insurance coverage
            .AddSelfCoverage()

            // Add encounter (ambulatory visit)
            .AddWellnessVisit("Annual physical exam")

            // Add vital signs observations (weight, height, blood pressure)
            .AddObservation(FhirCode.Observations.BodyWeight, 85m, "kg")
            .AddObservation(FhirCode.Observations.BodyHeight, 175m, "cm")
            .AddObservation(FhirCode.Observations.BloodPressureSystolic, 140m, "mmHg")

            // Add chronic condition
            .AddConditionOnset(FhirCode.Conditions.Hypertension, severity: 2, assignToAttribute: "hypertension")

            // Add medication for condition
            .AddMedicationOrder(
                FhirCode.Medications.Lisinopril10mg,
                isChronic: true,
                frequency: "daily",
                reasonCode: FhirCode.Conditions.Hypertension)

            // Add diagnostic report (metabolic panel)
            .AddComprehensiveMetabolicPanel()

            // Add immunization
            .AddInfluenzaVaccine()

            // Add allergy
            .AddPenicillinAllergy()

            // Add procedure
            .AddColonoscopy("Normal findings")

            // Add service request (lab order)
            .AddLipidPanelOrder()

            // Add goal
            .AddBloodPressureControlGoal(systolic: 130)

            // Add care plan
            .AddHypertensionManagementPlan()

            // Add care team
            .AddCareTeam("Hypertension Care Team", status: "active")

            .Build();

        // Package results into structured data object
        var data = new PatientCompartmentData
        {
            Tag = tag,
            Patient = scenario.Patient!,
            Observations = [.. scenario.Observations],
            Encounters = [.. scenario.Encounters],
            Conditions = [.. scenario.Conditions],
            Medications = [.. scenario.Medications],
            DiagnosticReports = [.. scenario.DiagnosticReports],
            Immunizations = [.. scenario.Immunizations],
            Allergies = [.. scenario.Allergies],
            Procedures = [.. scenario.Procedures],
            ServiceRequests = [.. scenario.ServiceRequests],
            Goals = [.. scenario.Goals],
            CarePlans = [.. scenario.CarePlans],
            CareTeams = [.. scenario.CareTeams],
            Coverages = [.. scenario.Coverages],
            Practitioners = [.. scenario.Practitioners],
            Organizations = [.. scenario.Organizations],
            AllResources = [.. scenario.AllResources]
        };

        return data;
    }

    /// <summary>
    /// Creates a minimal patient compartment with only observations.
    /// Useful for focused compartment search tests.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <param name="tag">A unique tag to isolate test data.</param>
    /// <param name="observationCount">Number of observations to create (default: 3).</param>
    /// <returns>A <see cref="PatientCompartmentData"/> with patient and observations only.</returns>
    public static PatientCompartmentData CreateMinimalCompartmentScenario(
        this IFhirSchemaProvider schemaProvider,
        string tag,
        int observationCount = 3)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(tag);

        var builder = new ScenarioBuilder(schemaProvider)
            .WithName("Minimal Patient Compartment")
            .WithTag(tag)
            .WithResolvedReferences()
            .WithSeattlePatient(p => p.WithFamilyName("MinimalPatient"));

        // Add requested number of observations
        for (int i = 0; i < observationCount; i++)
        {
            builder.AddObservation(
                FhirCode.Observations.BodyWeight,
                80m + i,
                "kg");
        }

        var scenario = builder.Build();

        return new PatientCompartmentData
        {
            Tag = tag,
            Patient = scenario.Patient!,
            Observations = [.. scenario.Observations],
            AllResources = [.. scenario.AllResources]
        };
    }
}

/// <summary>
/// Contains all test data and resource references for patient compartment search tests.
/// </summary>
public sealed class PatientCompartmentData
{
    /// <summary>
    /// Gets the test isolation tag applied to all resources.
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Gets the patient resource (compartment owner).
    /// </summary>
    public required ResourceJsonNode Patient { get; init; }

    /// <summary>
    /// Gets all observation resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Observations { get; init; } = [];

    /// <summary>
    /// Gets all encounter resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Encounters { get; init; } = [];

    /// <summary>
    /// Gets all condition resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Conditions { get; init; } = [];

    /// <summary>
    /// Gets all medication request resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Medications { get; init; } = [];

    /// <summary>
    /// Gets all diagnostic report resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> DiagnosticReports { get; init; } = [];

    /// <summary>
    /// Gets all immunization resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Immunizations { get; init; } = [];

    /// <summary>
    /// Gets all allergy intolerance resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Allergies { get; init; } = [];

    /// <summary>
    /// Gets all procedure resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Procedures { get; init; } = [];

    /// <summary>
    /// Gets all service request resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> ServiceRequests { get; init; } = [];

    /// <summary>
    /// Gets all goal resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Goals { get; init; } = [];

    /// <summary>
    /// Gets all care plan resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> CarePlans { get; init; } = [];

    /// <summary>
    /// Gets all care team resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> CareTeams { get; init; } = [];

    /// <summary>
    /// Gets all coverage resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Coverages { get; init; } = [];

    /// <summary>
    /// Gets all practitioner resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Practitioners { get; init; } = [];

    /// <summary>
    /// Gets all organization resources in the patient compartment.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<ResourceJsonNode> Organizations { get; init; } = [];

    /// <summary>
    /// Gets all resources in dependency order for batch/transaction creation.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public required List<ResourceJsonNode> AllResources { get; init; }
}
