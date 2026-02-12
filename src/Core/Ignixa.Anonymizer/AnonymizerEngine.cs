// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Pipeline;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Anonymizer;

/// <summary>
/// Engine for anonymizing FHIR resources using configurable rules and processors.
/// </summary>
public class AnonymizerEngine : IAnonymizerEngine
{
    private readonly IAnonymizerPipeline _pipeline;
    private readonly AnonymizerOptions _options;
    private readonly IFhirSchemaProvider _schema;
    private readonly ILogger<AnonymizerEngine> _logger;

    /// <summary>
    /// Creates a new AnonymizerEngine with dependency injection.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="pipeline">The anonymization pipeline.</param>
    /// <param name="schema">The FHIR schema provider.</param>
    /// <param name="logger">The logger.</param>
    public AnonymizerEngine(
        IOptions<AnonymizerOptions> options,
        IAnonymizerPipeline pipeline,
        IFhirSchemaProvider schema,
        ILogger<AnonymizerEngine> logger)
    {
        _options = options.Value;
        _pipeline = pipeline;
        _schema = schema;
        _logger = logger;
        _logger.LogDebug("AnonymizerEngine initialized via DI for FHIR version {FhirVersion}", _schema.Version);
    }

    /// <inheritdoc />
    public async ValueTask<Result<AnonymizationResult>> AnonymizeAsync(
        string resourceJson,
        RequestOptions? settings = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resource = ResourceJsonNode.Parse(resourceJson);
            return await AnonymizeAsync(resource, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse resource JSON");
            return Result<AnonymizationResult>.Failure(new AnonymizerError(
                "PARSE_ERROR",
                $"Failed to parse resource JSON: {ex.Message}",
                Exception: ex));
        }
    }

    /// <inheritdoc />
    public async ValueTask<Result<AnonymizationResult>> AnonymizeAsync(
        ResourceJsonNode resource,
        RequestOptions? settings = null,
        CancellationToken cancellationToken = default)
    {
        var element = resource.ToElement(_schema);
        var resourceId = element.Scalar("id")?.ToString() ?? "unknown";
        _logger.LogDebug("Anonymizing resource {ResourceType}/{ResourceId}", resource.ResourceType, resourceId);

        var context = new AnonymizerContext(
            resource,
            element,
            _schema,
            settings ?? new RequestOptions(),
            _options);

        return await _pipeline.ExecuteAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Result<AnonymizationResult>> AnonymizeManyAsync(
        IAsyncEnumerable<ResourceJsonNode> resources,
        RequestOptions? settings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var resource in resources.WithCancellation(cancellationToken))
        {
            yield return await AnonymizeAsync(resource, settings, cancellationToken);
        }
    }
}
