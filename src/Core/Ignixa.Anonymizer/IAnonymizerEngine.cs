// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Anonymizer;

/// <summary>
/// Settings for controlling anonymization behavior per request.
/// </summary>
public sealed record AnonymizerSettings
{
    /// <summary>
    /// Whether to format output JSON with indentation.
    /// </summary>
    public bool IsPrettyOutput { get; init; }

    /// <summary>
    /// Whether to validate input FHIR resources before anonymization.
    /// </summary>
    public bool ValidateInput { get; init; }

    /// <summary>
    /// Whether to validate output FHIR resources after anonymization.
    /// </summary>
    public bool ValidateOutput { get; init; }
}

/// <summary>
/// Engine for anonymizing FHIR resources using configurable rules and processors.
/// </summary>
public interface IAnonymizerEngine
{
    /// <summary>
    /// Anonymizes a FHIR resource from JSON string asynchronously.
    /// </summary>
    /// <param name="resourceJson">The FHIR resource as a JSON string.</param>
    /// <param name="schema">The FHIR schema provider for parsing.</param>
    /// <param name="settings">Optional per-request settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the anonymization result or an error.</returns>
    ValueTask<Result<AnonymizationResult>> AnonymizeAsync(
        string resourceJson,
        IFhirSchemaProvider schema,
        AnonymizerSettings? settings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymizes a FHIR resource from parsed nodes asynchronously.
    /// </summary>
    /// <param name="resource">The parsed resource node.</param>
    /// <param name="element">The root element.</param>
    /// <param name="schema">The FHIR schema provider.</param>
    /// <param name="settings">Optional per-request settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the anonymization result or an error.</returns>
    ValueTask<Result<AnonymizationResult>> AnonymizeAsync(
        ResourceJsonNode resource,
        IElement element,
        IFhirSchemaProvider schema,
        AnonymizerSettings? settings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymizes a stream of FHIR resources asynchronously (for bulk processing).
    /// </summary>
    /// <param name="resources">Async stream of FHIR resource JSON strings.</param>
    /// <param name="schema">The FHIR schema provider for parsing.</param>
    /// <param name="settings">Optional per-request settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async stream of anonymization results (success or failure per resource).</returns>
    IAsyncEnumerable<Result<AnonymizationResult>> AnonymizeManyAsync(
        IAsyncEnumerable<string> resources,
        IFhirSchemaProvider schema,
        AnonymizerSettings? settings = null,
        CancellationToken cancellationToken = default);
}
