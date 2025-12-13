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
/// Fluent builder for generating MedicationRequest resources with medication references or codeable concepts.
/// Supports prescribing practitioners, medication codes, and temporal ordering.
/// </summary>
/// <remarks>
/// <para>
/// This builder provides comprehensive support for creating test medication requests with:
/// </para>
/// <list type="bullet">
/// <item><description>Status (active, completed, cancelled, etc.)</description></item>
/// <item><description>Intent (order, plan, proposal)</description></item>
/// <item><description>Medication as CodeableConcept or Reference</description></item>
/// <item><description>Patient subject reference</description></item>
/// <item><description>Practitioner requester reference</description></item>
/// <item><description>Temporal tracking via authoredOn</description></item>
/// </list>
/// <para><strong>Example Usage - Basic Order with Codeable Concept:</strong></para>
/// <code>
/// var request = MedicationRequestBuilder.Create(schemaProvider)
///     .WithStatus("active")
///     .WithIntent("order")
///     .WithSubject(patientId)
///     .WithMedicationCodeableConcept("16590-619-30", "http://snomed.info/sct", "Amoxicillin 500mg")
///     .WithRequester(practitionerId)
///     .WithTag(tag)
///     .Build();
/// </code>
/// <para><strong>Example Usage - Medication Reference:</strong></para>
/// <code>
/// var request = MedicationRequestBuilder.Create(schemaProvider)
///     .WithStatus("completed")
///     .WithIntent("order")
///     .WithSubject(patientId)
///     .WithMedicationReference(medicationId)
///     .WithAuthoredOn("2023-01-15T10:30:00Z")
///     .Build();
/// </code>
/// <para><strong>Example Usage - Minimal Request:</strong></para>
/// <code>
/// var request = MedicationRequestBuilder.Create(schemaProvider)
///     .WithSubject(patientId)
///     .WithMedicationCodeableConcept("aspirin", "http://example.org")
///     .Build();
/// </code>
/// </remarks>
public sealed class MedicationRequestBuilder : FhirResourceBuilder<MedicationRequestBuilder>
{
    // Core fields
    private string _status = "active";
    private string _intent = "order";

    // Subject
    private string? _subjectId;

    // Medication (mutually exclusive)
    private string? _medicationCode;
    private string? _medicationSystem;
    private string? _medicationDisplay;
    private string? _medicationReferenceId;

    // Requester
    private string? _requesterId;

    // Temporal
    private string? _authoredOn;

    private MedicationRequestBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new MedicationRequestBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for version-appropriate resource generation.</param>
    /// <returns>A new MedicationRequestBuilder instance.</returns>
    /// <example>
    /// <code>
    /// var builder = MedicationRequestBuilder.Create(schemaProvider);
    /// </code>
    /// </example>
    public static MedicationRequestBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new MedicationRequestBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the medication request status.
    /// </summary>
    /// <param name="status">The status code (e.g., "active", "completed", "cancelled", "on-hold", "stopped").</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithStatus("completed")
    /// .WithStatus("active")
    /// </code>
    /// </example>
    public MedicationRequestBuilder WithStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
        return this;
    }

    /// <summary>
    /// Sets the medication request intent.
    /// </summary>
    /// <param name="intent">The intent code (e.g., "order", "plan", "proposal", "instance-order").</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithIntent("order")
    /// .WithIntent("plan")
    /// </code>
    /// </example>
    public MedicationRequestBuilder WithIntent(string intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        _intent = intent;
        return this;
    }

    /// <summary>
    /// Sets the subject reference (Patient).
    /// </summary>
    /// <param name="patientId">The patient resource ID (not the full reference path).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithSubject(patient.Id!)
    /// // Generates: "subject": { "reference": "Patient/{id}" }
    /// </code>
    /// </example>
    public MedicationRequestBuilder WithSubject(string patientId)
    {
        ArgumentNullException.ThrowIfNull(patientId);
        _subjectId = patientId;
        return this;
    }

    /// <summary>
    /// Sets the medication as a CodeableConcept.
    /// Mutually exclusive with <see cref="WithMedicationReference"/>.
    /// </summary>
    /// <param name="code">The medication code (e.g., "16590-619-30" for Amoxicillin).</param>
    /// <param name="system">The code system URI (e.g., "http://snomed.info/sct").</param>
    /// <param name="display">Optional display text for the medication.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithMedicationCodeableConcept("16590-619-30", "http://snomed.info/sct", "Amoxicillin 500mg")
    /// .WithMedicationCodeableConcept("aspirin", "http://example.org")
    /// </code>
    /// </example>
    public MedicationRequestBuilder WithMedicationCodeableConcept(string code, string system, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(system);
        _medicationCode = code;
        _medicationSystem = system;
        _medicationDisplay = display;

        // Clear medication reference (mutually exclusive)
        _medicationReferenceId = null;

        return this;
    }

    /// <summary>
    /// Sets the medication as a Reference to a Medication resource.
    /// Mutually exclusive with <see cref="WithMedicationCodeableConcept"/>.
    /// </summary>
    /// <param name="medicationId">The medication resource ID.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithMedicationReference(medication.Id!)
    /// // Generates: "medicationReference": { "reference": "Medication/{id}" }
    /// </code>
    /// </example>
    public MedicationRequestBuilder WithMedicationReference(string medicationId)
    {
        ArgumentNullException.ThrowIfNull(medicationId);
        _medicationReferenceId = medicationId;

        // Clear codeable concept (mutually exclusive)
        _medicationCode = null;
        _medicationSystem = null;
        _medicationDisplay = null;

        return this;
    }

    /// <summary>
    /// Sets the requester reference (typically a Practitioner).
    /// </summary>
    /// <param name="practitionerId">The practitioner resource ID.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithRequester(practitioner.Id!)
    /// // Generates: "requester": { "reference": "Practitioner/{id}" }
    /// </code>
    /// </example>
    public MedicationRequestBuilder WithRequester(string practitionerId)
    {
        ArgumentNullException.ThrowIfNull(practitionerId);
        _requesterId = practitionerId;
        return this;
    }

    /// <summary>
    /// Sets the authoredOn timestamp (when the prescription was written).
    /// </summary>
    /// <param name="dateTime">ISO 8601 date/time string (e.g., "2023-01-15T10:30:00Z").</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithAuthoredOn("2023-01-15T10:30:00Z")
    /// .WithAuthoredOn("2023-01-15")
    /// </code>
    /// </example>
    public MedicationRequestBuilder WithAuthoredOn(string dateTime)
    {
        ArgumentNullException.ThrowIfNull(dateTime);
        _authoredOn = dateTime;
        return this;
    }

    /// <summary>
    /// Builds the MedicationRequest resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the MedicationRequest resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if required fields (status, intent, subject, medication) are not set.</exception>
    /// <example>
    /// <code>
    /// var request = MedicationRequestBuilder.Create(schemaProvider)
    ///     .WithSubject(patientId)
    ///     .WithMedicationCodeableConcept("aspirin", "http://example.org")
    ///     .Build();
    /// </code>
    /// </example>
    public override ResourceJsonNode Build()
    {
        // Validate required fields
        if (_subjectId is null)
        {
            throw new InvalidOperationException("MedicationRequest subject is required. Call WithSubject() before Build().");
        }

        if (_medicationCode is null && _medicationReferenceId is null)
        {
            throw new InvalidOperationException("Medication is required. Call WithMedicationCodeableConcept() or WithMedicationReference() before Build().");
        }

        var requestJson = new JsonObject
        {
            ["resourceType"] = "MedicationRequest",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["status"] = _status,
            ["intent"] = _intent,
            ["subject"] = CreateReference("Patient", _subjectId)
        };

        // Medication (mutually exclusive)
        if (_medicationReferenceId is not null)
        {
            requestJson["medicationReference"] = CreateReference("Medication", _medicationReferenceId);
        }
        else if (_medicationCode is not null && _medicationSystem is not null)
        {
            requestJson["medicationCodeableConcept"] = CreateCodeableConcept(_medicationCode, _medicationSystem, _medicationDisplay);
        }

        // Optional fields
        if (_requesterId is not null)
        {
            requestJson["requester"] = CreateReference("Practitioner", _requesterId);
        }

        if (_authoredOn is not null)
        {
            requestJson["authoredOn"] = _authoredOn;
        }

        var json = requestJson.ToJsonString();
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }
}
