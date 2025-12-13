// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Fluent builder for generating MedicationDispense resources.
/// Supports medication coding, references, prescriptions, and dispensing details.
/// </summary>
/// <remarks>
/// <para>
/// This builder provides comprehensive support for creating test medication dispenses with:
/// </para>
/// <list type="bullet">
/// <item><description>Status (completed, in-progress, stopped, etc.)</description></item>
/// <item><description>Medication as CodeableConcept or Reference (mutually exclusive)</description></item>
/// <item><description>Subject reference (typically Patient)</description></item>
/// <item><description>Authorizing prescription references (MedicationRequest)</description></item>
/// <item><description>Performer references (Practitioner, Organization)</description></item>
/// <item><description>Timing (whenHandedOver)</description></item>
/// </list>
/// <para><strong>Example Usage - Basic Dispense with CodeableConcept:</strong></para>
/// <code>
/// var dispense = MedicationDispenseBuilder.Create(schemaProvider)
///     .WithStatus("completed")
///     .WithSubject(patientId)
///     .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct", "Aspirin")
///     .WithTag(tag)
///     .Build();
/// </code>
/// <para><strong>Example Usage - Dispense with Medication Reference:</strong></para>
/// <code>
/// var dispense = MedicationDispenseBuilder.Create(schemaProvider)
///     .WithStatus("in-progress")
///     .WithSubject(patientId)
///     .WithMedicationReference(medicationId)
///     .Build();
/// </code>
/// <para><strong>Example Usage - Complete Dispense with Prescription:</strong></para>
/// <code>
/// var dispense = MedicationDispenseBuilder.Create(schemaProvider)
///     .WithStatus("completed")
///     .WithSubject(patientId)
///     .WithMedicationCodeableConcept("197361", "http://www.nlm.nih.gov/research/umls/rxnorm", "Lisinopril")
///     .WithAuthorizingPrescription(medicationRequestId)
///     .WithPerformer(practitionerId)
///     .WithWhenHandedOver("2024-01-15T10:30:00Z")
///     .Build();
/// </code>
/// </remarks>
public sealed class MedicationDispenseBuilder : FhirResourceBuilder<MedicationDispenseBuilder>
{
    // Core fields
    private string _status = "completed";

    // Subject
    private string? _subjectId;

    // Medication (mutually exclusive)
    private string? _medicationCode;
    private string? _medicationSystem;
    private string? _medicationDisplay;
    private string? _medicationReferenceId;

    // Prescription
    private readonly List<string> _authorizingPrescriptionIds = [];

    // Performer
    private readonly List<(string ResourceType, string Id)> _performers = [];

    // Timing
    private string? _whenHandedOver;

    private MedicationDispenseBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new MedicationDispenseBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for version-appropriate resource generation.</param>
    /// <returns>A new MedicationDispenseBuilder instance.</returns>
    /// <example>
    /// <code>
    /// var builder = MedicationDispenseBuilder.Create(schemaProvider);
    /// </code>
    /// </example>
    public static MedicationDispenseBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new MedicationDispenseBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the dispense status.
    /// Default is "completed" if not specified.
    /// </summary>
    /// <param name="status">The status code (e.g., "completed", "in-progress", "stopped", "on-hold").</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithStatus("completed")
    /// .WithStatus("in-progress")
    /// </code>
    /// </example>
    public MedicationDispenseBuilder WithStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
        return this;
    }

    /// <summary>
    /// Sets the subject reference (typically a Patient).
    /// </summary>
    /// <param name="patientId">The patient resource ID (not the full reference path).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithSubject(patient.Id!)
    /// // Generates: "subject": { "reference": "Patient/{id}" }
    /// </code>
    /// </example>
    public MedicationDispenseBuilder WithSubject(string patientId)
    {
        ArgumentNullException.ThrowIfNull(patientId);
        _subjectId = patientId;
        return this;
    }

    /// <summary>
    /// Sets the medication as a CodeableConcept.
    /// Mutually exclusive with <see cref="WithMedicationReference"/>.
    /// </summary>
    /// <param name="code">The medication code (e.g., "108505002" for Aspirin from SNOMED CT).</param>
    /// <param name="system">The code system (e.g., "http://snomed.info/sct", "http://www.nlm.nih.gov/research/umls/rxnorm").</param>
    /// <param name="display">Optional display text for the medication.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // SNOMED CT medication
    /// .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct", "Aspirin")
    ///
    /// // RxNorm medication
    /// .WithMedicationCodeableConcept("197361", "http://www.nlm.nih.gov/research/umls/rxnorm", "Lisinopril")
    /// </code>
    /// </example>
    public MedicationDispenseBuilder WithMedicationCodeableConcept(string code, string system, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(system);
        _medicationCode = code;
        _medicationSystem = system;
        _medicationDisplay = display;

        // Clear reference variant (mutually exclusive)
        _medicationReferenceId = null;

        return this;
    }

    /// <summary>
    /// Sets the medication as a Reference to a Medication resource.
    /// Mutually exclusive with <see cref="WithMedicationCodeableConcept"/>.
    /// </summary>
    /// <param name="medicationId">The medication resource ID (not the full reference path).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithMedicationReference(medication.Id!)
    /// // Generates: "medicationReference": { "reference": "Medication/{id}" }
    /// </code>
    /// </example>
    public MedicationDispenseBuilder WithMedicationReference(string medicationId)
    {
        ArgumentNullException.ThrowIfNull(medicationId);
        _medicationReferenceId = medicationId;

        // Clear CodeableConcept variant (mutually exclusive)
        _medicationCode = null;
        _medicationSystem = null;
        _medicationDisplay = null;

        return this;
    }

    /// <summary>
    /// Adds an authorizing prescription reference (MedicationRequest).
    /// Can be called multiple times to add multiple prescriptions.
    /// </summary>
    /// <param name="medicationRequestId">The MedicationRequest resource ID.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithAuthorizingPrescription(medicationRequest.Id!)
    /// // Generates: "authorizingPrescription": [{ "reference": "MedicationRequest/{id}" }]
    /// </code>
    /// </example>
    public MedicationDispenseBuilder WithAuthorizingPrescription(string medicationRequestId)
    {
        ArgumentNullException.ThrowIfNull(medicationRequestId);
        _authorizingPrescriptionIds.Add(medicationRequestId);
        return this;
    }

    /// <summary>
    /// Adds a performer reference (who performed the dispense).
    /// Can be called multiple times for multiple performers.
    /// </summary>
    /// <param name="practitionerId">The Practitioner resource ID.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithPerformer(practitioner.Id!)
    /// // Generates: "performer": [{ "actor": { "reference": "Practitioner/{id}" } }]
    /// </code>
    /// </example>
    public MedicationDispenseBuilder WithPerformer(string practitionerId)
    {
        ArgumentNullException.ThrowIfNull(practitionerId);
        _performers.Add(("Practitioner", practitionerId));
        return this;
    }

    /// <summary>
    /// Sets the whenHandedOver timestamp (when the medication was given to the patient).
    /// </summary>
    /// <param name="dateTime">ISO 8601 date/time string (e.g., "2024-01-15T10:30:00Z").</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithWhenHandedOver("2024-01-15T10:30:00Z")
    /// </code>
    /// </example>
    public MedicationDispenseBuilder WithWhenHandedOver(string dateTime)
    {
        ArgumentNullException.ThrowIfNull(dateTime);
        _whenHandedOver = dateTime;
        return this;
    }

    /// <summary>
    /// Builds the MedicationDispense resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the MedicationDispense resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if required fields (medication) are not set.</exception>
    /// <example>
    /// <code>
    /// var dispense = MedicationDispenseBuilder.Create(schemaProvider)
    ///     .WithStatus("completed")
    ///     .WithSubject(patientId)
    ///     .WithMedicationCodeableConcept("108505002", "http://snomed.info/sct", "Aspirin")
    ///     .Build();
    /// </code>
    /// </example>
    public override ResourceJsonNode Build()
    {
        var dispenseJson = new JsonObject
        {
            ["resourceType"] = "MedicationDispense",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["status"] = _status
        };

        // Medication is required (either CodeableConcept or Reference)
        if (_medicationCode is not null && _medicationSystem is not null)
        {
            // medicationCodeableConcept variant
            dispenseJson["medicationCodeableConcept"] = CreateCodeableConcept(
                _medicationCode,
                _medicationSystem,
                _medicationDisplay);
        }
        else if (_medicationReferenceId is not null)
        {
            // medicationReference variant
            dispenseJson["medicationReference"] = CreateReference("Medication", _medicationReferenceId);
        }
        else
        {
            throw new InvalidOperationException(
                "Medication is required. Call WithMedicationCodeableConcept() or WithMedicationReference() before Build().");
        }

        // Subject
        if (_subjectId is not null)
        {
            dispenseJson["subject"] = CreateReference("Patient", _subjectId);
        }

        // Authorizing prescriptions
        if (_authorizingPrescriptionIds.Count > 0)
        {
            dispenseJson["authorizingPrescription"] = BuildAuthorizingPrescriptions();
        }

        // Performers
        if (_performers.Count > 0)
        {
            dispenseJson["performer"] = BuildPerformers();
        }

        // Timing
        if (_whenHandedOver is not null)
        {
            dispenseJson["whenHandedOver"] = _whenHandedOver;
        }

        var json = dispenseJson.ToJsonString();
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }

    private JsonArray BuildAuthorizingPrescriptions()
    {
        var prescriptions = new JsonArray();

        foreach (var prescriptionId in _authorizingPrescriptionIds)
        {
            prescriptions.Add(CreateReference("MedicationRequest", prescriptionId));
        }

        return prescriptions;
    }

    private JsonArray BuildPerformers()
    {
        var performers = new JsonArray();

        foreach (var (resourceType, id) in _performers)
        {
            performers.Add(new JsonObject
            {
                ["actor"] = CreateReference(resourceType, id)
            });
        }

        return performers;
    }
}
