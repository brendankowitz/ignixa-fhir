// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Extensions;
using Ignixa.DeId.Models;
using Ignixa.DeId.Pipeline;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.DeId;

/// <summary>
/// Engine for de-identifying FHIR resources using configurable rules and processors.
/// </summary>
public class DeIdEngine : IDeIdEngine
{
    private readonly IDeIdPipeline _pipeline;
    private readonly DeIdOptions _options;
    private readonly IFhirSchemaProvider _schema;
    private readonly ILogger<DeIdEngine> _logger;

    /// <summary>
    /// Creates a new DeIdEngine with dependency injection.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="pipeline">The de-identification pipeline.</param>
    /// <param name="schema">The FHIR schema provider.</param>
    /// <param name="logger">The logger.</param>
    public DeIdEngine(
        IOptions<DeIdOptions> options,
        IDeIdPipeline pipeline,
        IFhirSchemaProvider schema,
        ILogger<DeIdEngine> logger)
    {
        _options = options.Value;
        _pipeline = pipeline;
        _schema = schema;
        _logger = logger;
        _logger.LogDebug("DeIdEngine initialized via DI for FHIR version {FhirVersion}", _schema.Version);
    }

    /// <inheritdoc />
    public async ValueTask<Result<DeIdResult>> DeidentifyAsync(
        string resourceJson,
        RequestOptions? settings = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resource = ResourceJsonNode.Parse(resourceJson);
            return await DeidentifyAsync(resource, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse resource JSON");
            return Result<DeIdResult>.Failure(new DeIdError(
                "PARSE_ERROR",
                $"Failed to parse resource JSON: {ex.Message}",
                Exception: ex));
        }
    }

    /// <inheritdoc />
    public async ValueTask<Result<DeIdResult>> DeidentifyAsync(
        ResourceJsonNode resource,
        RequestOptions? settings = null,
        CancellationToken cancellationToken = default)
    {
        var element = resource.ToElement(_schema);
        var resourceId = element.Scalar("id")?.ToString() ?? "unknown";
        _logger.LogDebug("De-identifying resource {ResourceType}/{ResourceId}", resource.ResourceType, resourceId);

        var context = new DeIdContext(
            resource,
            element,
            _schema,
            settings ?? new RequestOptions(),
            _options);

        return await _pipeline.ExecuteAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Result<DeIdResult>> DeidentifyManyAsync(
        IAsyncEnumerable<ResourceJsonNode> resources,
        RequestOptions? settings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var resource in resources.WithCancellation(cancellationToken))
        {
            yield return await DeidentifyAsync(resource, settings, cancellationToken);
        }
    }
}
