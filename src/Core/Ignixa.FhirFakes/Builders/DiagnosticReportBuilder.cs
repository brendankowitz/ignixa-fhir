// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Fluent builder for generating DiagnosticReport resources.
/// Provides clean API for test data setup without manual JSON manipulation.
/// </summary>
/// <remarks>
/// <para>
/// DiagnosticReport represents the findings and interpretation of diagnostic tests.
/// This builder supports creating diagnostic reports with:
/// </para>
/// <list type="bullet">
/// <item><description>Status (final, preliminary, etc.)</description></item>
/// <item><description>Diagnostic code (LOINC, SNOMED CT)</description></item>
/// <item><description>Subject reference (typically Patient)</description></item>
/// <item><description>Result references (Observation resources)</description></item>
/// </list>
/// <para><strong>Basic Usage - Lab Report with Single Result:</strong></para>
/// <code>
/// var report = DiagnosticReportBuilder.Create(schemaProvider)
///     .WithStatus("final")
///     .WithCode("58410-2", "http://loinc.org", "Complete blood count")
///     .WithSubject(patientId)
///     .WithResult(observationId)
///     .Build();
/// </code>
/// <para><strong>Comprehensive Report with Multiple Results:</strong></para>
/// <code>
/// var report = DiagnosticReportBuilder.Create(schemaProvider)
///     .WithId("report-123")
///     .WithStatus("final")
///     .WithCode("24331-1", "http://loinc.org", "Lipid panel")
///     .WithSubject(patientId)
///     .WithResults(cholesterolObsId, hdlObsId, ldlObsId, triglyceridesObsId)
///     .WithTag(testTag)
///     .Build();
/// </code>
/// <para><strong>Radiology Report:</strong></para>
/// <code>
/// var report = DiagnosticReportBuilder.Create(schemaProvider)
///     .WithStatus("final")
///     .WithCode("36554-4", "http://loinc.org", "Chest X-ray")
///     .WithSubject(patientId)
///     .WithResult(imagingObservationId)
///     .Build();
/// </code>
/// <para><strong>Preliminary Report (Partial Results):</strong></para>
/// <code>
/// var report = DiagnosticReportBuilder.Create(schemaProvider)
///     .WithStatus("preliminary")
///     .WithCode("24323-8", "http://loinc.org", "Comprehensive metabolic panel")
///     .WithSubject(patientId)
///     .WithResults(glucoseObsId, sodiumObsId) // More results pending
///     .Build();
/// </code>
/// </remarks>
public sealed class DiagnosticReportBuilder : FhirResourceBuilder<DiagnosticReportBuilder>
{
    // Core fields
    private string _status = "final";

    // Code (required)
    private string? _codeCode;
    private string? _codeSystem;
    private string? _codeDisplay;

    // Subject (optional)
    private string? _subjectId;

    // Results (optional)
    private readonly List<string> _resultObservationIds = [];

    private DiagnosticReportBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new DiagnosticReportBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for version-appropriate resource generation.</param>
    /// <returns>A new DiagnosticReportBuilder instance.</returns>
    /// <example>
    /// <code>
    /// var builder = DiagnosticReportBuilder.Create(schemaProvider);
    /// </code>
    /// </example>
    public static DiagnosticReportBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new DiagnosticReportBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the diagnostic report status.
    /// Default is "final" if not specified.
    /// </summary>
    /// <param name="status">The status code (e.g., "final", "preliminary", "amended", "corrected", "cancelled").</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithStatus("preliminary") // Results not yet complete
    /// .WithStatus("final")       // Complete and verified
    /// .WithStatus("amended")     // Modified after final
    /// </code>
    /// </example>
    public DiagnosticReportBuilder WithStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
        return this;
    }

    /// <summary>
    /// Sets the diagnostic code using LOINC, SNOMED CT, or other coding system.
    /// This identifies what type of diagnostic report this is (e.g., lab panel, imaging study).
    /// </summary>
    /// <param name="code">The code value (e.g., "58410-2" for CBC, "24331-1" for lipid panel).</param>
    /// <param name="system">The code system URI (e.g., "http://loinc.org", "http://snomed.info/sct").</param>
    /// <param name="display">Optional display text for the code.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Lab panel
    /// .WithCode("24331-1", "http://loinc.org", "Lipid panel")
    ///
    /// // Radiology
    /// .WithCode("36554-4", "http://loinc.org", "Chest X-ray")
    ///
    /// // Pathology
    /// .WithCode("60568-3", "http://loinc.org", "Pathology Synoptic report")
    /// </code>
    /// </example>
    public DiagnosticReportBuilder WithCode(string code, string system, string? display = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(system);
        _codeCode = code;
        _codeSystem = system;
        _codeDisplay = display;
        return this;
    }

    /// <summary>
    /// Sets the subject reference (typically a Patient).
    /// The subject is the person or entity this report is about.
    /// </summary>
    /// <param name="patientId">The patient resource ID (not the full reference path).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// .WithSubject(patient.Id!)
    /// // Generates: "subject": { "reference": "Patient/{id}" }
    /// </code>
    /// </example>
    public DiagnosticReportBuilder WithSubject(string patientId)
    {
        ArgumentNullException.ThrowIfNull(patientId);
        _subjectId = patientId;
        return this;
    }

    /// <summary>
    /// Adds a single observation to the result array.
    /// Results are references to Observation resources containing the actual test values.
    /// Can be called multiple times to add multiple results.
    /// </summary>
    /// <param name="observationId">The observation resource ID (not the full reference path).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Add single result
    /// .WithResult(hemoglobinObservationId)
    ///
    /// // Add multiple results via chaining
    /// .WithResult(obs1Id)
    /// .WithResult(obs2Id)
    /// .WithResult(obs3Id)
    /// </code>
    /// </example>
    public DiagnosticReportBuilder WithResult(string observationId)
    {
        ArgumentNullException.ThrowIfNull(observationId);
        _resultObservationIds.Add(observationId);
        return this;
    }

    /// <summary>
    /// Adds multiple observations to the result array.
    /// Results are references to Observation resources containing the actual test values.
    /// This is a convenience method for adding all results at once.
    /// </summary>
    /// <param name="observationIds">Array of observation resource IDs (not full reference paths).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// // Lipid panel with 4 component observations
    /// .WithResults(
    ///     totalCholesterolObsId,
    ///     hdlCholesterolObsId,
    ///     ldlCholesterolObsId,
    ///     triglyceridesObsId)
    ///
    /// // Single result (equivalent to WithResult)
    /// .WithResults(singleObservationId)
    /// </code>
    /// </example>
    public DiagnosticReportBuilder WithResults(params string[] observationIds)
    {
        ArgumentNullException.ThrowIfNull(observationIds);

        foreach (var id in observationIds)
        {
            ArgumentNullException.ThrowIfNull(id);
            _resultObservationIds.Add(id);
        }

        return this;
    }

    /// <summary>
    /// Builds the DiagnosticReport resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the DiagnosticReport resource.</returns>
    /// <exception cref="InvalidOperationException">Thrown if required fields (code) are not set.</exception>
    /// <example>
    /// <code>
    /// var report = DiagnosticReportBuilder.Create(schemaProvider)
    ///     .WithCode("58410-2", "http://loinc.org", "Complete blood count")
    ///     .WithStatus("final")
    ///     .WithSubject(patientId)
    ///     .WithResults(wbcObsId, rbcObsId, hgbObsId)
    ///     .Build();
    /// </code>
    /// </example>
    public override ResourceJsonNode Build()
    {
        var reportJson = new JsonObject
        {
            ["resourceType"] = "DiagnosticReport",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["status"] = _status
        };

        // Code is required
        if (_codeCode is null || _codeSystem is null)
        {
            throw new InvalidOperationException("DiagnosticReport code is required. Call WithCode() before Build().");
        }

        reportJson["code"] = CreateCodeableConcept(_codeCode, _codeSystem, _codeDisplay);

        // Subject (optional but typical)
        if (_subjectId is not null)
        {
            reportJson["subject"] = CreateReference("Patient", _subjectId);
        }

        // Results (optional)
        if (_resultObservationIds.Count > 0)
        {
            reportJson["result"] = BuildResults();
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(reportJson);
    }

    private JsonArray BuildResults()
    {
        var results = new JsonArray();

        foreach (var observationId in _resultObservationIds)
        {
            results.Add(CreateReference("Observation", observationId));
        }

        return results;
    }
}
