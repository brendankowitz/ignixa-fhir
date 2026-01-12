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
/// Fluent builder for generating RiskAssessment resources.
/// Used for testing number search parameters with the 'probability' search parameter.
/// </summary>
/// <remarks>
/// RiskAssessment has a standard FHIR R4 number search parameter: 'probability'.
/// This is the recommended way to test number search functionality in E2E tests.
///
/// The probability search parameter targets: RiskAssessment.prediction.probability
/// which can be a decimal (0.0 to 1.0) or a Range with low/high values.
///
/// Example Usage:
/// <code>
/// var assessment = new RiskAssessmentBuilder(schemaProvider)
///     .WithSubject(patientId)
///     .WithProbability(0.75m)  // 75% probability
///     .WithTag(testTag)
///     .Build();
/// </code>
/// </remarks>
public sealed class RiskAssessmentBuilder : FhirResourceBuilder<RiskAssessmentBuilder>
{
    private string? _subjectId;
    private decimal? _probability;
    private string _outcomeCode = "risk";
    private string _outcomeDisplay = "Risk identified";

    /// <summary>
    /// Creates a new RiskAssessmentBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    public RiskAssessmentBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Sets the subject (patient) reference for the risk assessment.
    /// </summary>
    /// <param name="patientId">The patient resource ID.</param>
    /// <returns>This builder for method chaining.</returns>
    public RiskAssessmentBuilder WithSubject(string patientId)
    {
        ArgumentNullException.ThrowIfNull(patientId);
        _subjectId = patientId;
        return this;
    }

    /// <summary>
    /// Sets the probability value for the prediction.
    /// This value is searchable via the 'probability' search parameter.
    /// </summary>
    /// <param name="probability">Probability between 0.0 and 1.0.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when probability is not between 0.0 and 1.0.</exception>
    public RiskAssessmentBuilder WithProbability(decimal probability)
    {
        if (probability < 0 || probability > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), probability, "Probability must be between 0.0 and 1.0");
        }

        _probability = probability;
        return this;
    }

    /// <summary>
    /// Sets the outcome code and display for the prediction.
    /// </summary>
    /// <param name="code">The outcome code.</param>
    /// <param name="display">The outcome display text.</param>
    /// <returns>This builder for method chaining.</returns>
    public RiskAssessmentBuilder WithOutcome(string code, string display)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(display);
        _outcomeCode = code;
        _outcomeDisplay = display;
        return this;
    }

    /// <summary>
    /// Builds the RiskAssessment resource.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the RiskAssessment.</returns>
    public override ResourceJsonNode Build()
    {
        var riskAssessmentJson = new JsonObject
        {
            ["resourceType"] = "RiskAssessment",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["status"] = "final"
        };

        // Subject reference (required by spec but we make it optional for flexibility)
        if (!string.IsNullOrEmpty(_subjectId))
        {
            riskAssessmentJson["subject"] = CreateReference("Patient", _subjectId);
        }

        // Add prediction with probability if set
        if (_probability.HasValue)
        {
            var prediction = new JsonObject
            {
                ["outcome"] = CreateCodeableConcept(_outcomeCode, "http://example.org/outcomes", _outcomeDisplay),
                ["probabilityDecimal"] = _probability.Value
            };

            riskAssessmentJson["prediction"] = new JsonArray { prediction };
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(riskAssessmentJson);
    }
}
