// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Anonymizer;

/// <summary>
/// Engine for anonymizing FHIR resources using configurable rules and processors.
/// </summary>
public interface IAnonymizerEngine
{
    /// <summary>
    /// Anonymizes a FHIR resource from JSON string asynchronously.
    /// </summary>
    /// <param name="resourceJson">The FHIR resource as a JSON string.</param>
    /// <param name="settings">Optional per-request settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the anonymization result or an error.</returns>
    ValueTask<Result<AnonymizationResult>> AnonymizeAsync(
        string resourceJson,
        RequestOptions? settings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymizes a FHIR resource from parsed resource node asynchronously.
    /// Use this overload when you already have a parsed resource to avoid re-parsing.
    /// </summary>
    /// <param name="resource">The parsed resource node.</param>
    /// <param name="settings">Optional per-request settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the anonymization result or an error.</returns>
    ValueTask<Result<AnonymizationResult>> AnonymizeAsync(
        ResourceJsonNode resource,
        RequestOptions? settings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymizes a stream of FHIR resources asynchronously (for bulk processing).
    /// </summary>
    /// <param name="resources">Async stream of parsed FHIR resources.</param>
    /// <param name="settings">Optional per-request settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async stream of anonymization results (success or failure per resource).</returns>
    IAsyncEnumerable<Result<AnonymizationResult>> AnonymizeManyAsync(
        IAsyncEnumerable<ResourceJsonNode> resources,
        RequestOptions? settings = null,
        CancellationToken cancellationToken = default);
}
