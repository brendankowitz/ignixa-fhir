// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.Experimental.Transform;

/// <summary>
/// Handler for StructureMap $transform operation.
/// Executes FHIR Mapping Language transformations using the Ignixa.FhirMappingLanguage library.
///
/// Flow:
/// 1. Validate Content parameter (required)
/// 2. Resolve StructureMap (from URL, inline resource, or FML text)
/// 3. Parse to MapExpression AST
/// 4. Register supporting maps in MapRegistry
/// 5. Create MappingContext with FHIR resource and callbacks
/// 6. Execute transformation using MappingEvaluator
/// 7. Return transformed resource
/// </summary>
public class TransformResourceHandler(
    MapRegistryCache mapCache,
    MappingParser mappingParser,
    StructureMapParser structureMapParser,
    ConceptMapResolverService conceptMapService,
    FhirPathParser fhirPathParser,
    FhirPathEvaluator fhirPathEvaluator,
    FhirPathEvaluatorWithTimeout fhirPathEvaluatorWithTimeout,
    IFhirVersionContext versionContext,
    IFhirRequestContextAccessor contextAccessor,
    ILogger<TransformResourceHandler> logger) : IRequestHandler<TransformResourceCommand, ResourceJsonNode>
{
    // Schema instance for current transformation (set in HandleAsync)
    private ISchema? _currentSchema;

    public async Task<ResourceJsonNode> HandleAsync(
        TransformResourceCommand request,
        CancellationToken cancellationToken)
    {
        // Step 1: Validate required parameters
        if (request.Content == null)
        {
            throw new InvalidOperationException("Content parameter is required for $transform operation");
        }

        logger.LogInformation(
            "Executing StructureMap $transform operation on {ResourceType}/{ResourceId}",
            request.Content.ResourceType,
            request.Content.Id);

        // Step 2: Resolve the mapping (priority: SrcMaps → SourceMap → Source URL)
        var map = await ResolveMapAsync(request, cancellationToken);

        logger.LogDebug(
            "Resolved StructureMap: {MapUrl} (Identifier: {MapIdentifier})",
            map.Url,
            map.Identifier);

        // Step 3: Register supporting maps
        if (request.SupportingMaps != null)
        {
            RegisterSupportingMaps(request.SupportingMaps);
        }

        // Step 4: Determine target type from map's uses declarations
        var targetType = DetermineTargetType(map, request.Content.ResourceType);

        logger.LogDebug("Target resource type: {TargetType}", targetType);

        // Step 5: Create empty target resource
        var target = CreateResource(targetType);

        // Step 6: Get schema provider from FHIR request context
        var fhirContext = contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _currentSchema = versionContext.GetSchemaProvider(fhirContext.FhirVersion, fhirContext.TenantId);

        logger.LogDebug(
            "Using FHIR version {FhirVersion} for tenant {TenantId}",
            fhirContext.FhirVersion,
            fhirContext.TenantId);

        // Step 7: Convert resources to IElement
        var sourceElement = request.Content.ToElement(_currentSchema);
        var targetElement = target.ToElement(_currentSchema);

        // Step 8: Create evaluation context with callbacks
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict, // Fail fast on errors
            ResourceCreator = CreateResourceElement,
            ConceptMapResolver = (conceptMapUrl, sourceSystem, sourceCode) =>
            {
                // Synchronous wrapper for async ConceptMapResolverService
                // FML signature: (conceptMapUrl, sourceSystem, sourceCode)
                // Service signature: (sourceCode, sourceSystem, mapUrl, targetSystem)
                return conceptMapService.Translate(sourceCode, sourceSystem, conceptMapUrl, targetSystem: null);
            },
            Logger = msg => logger.LogDebug("Mapping execution: {Message}", msg),
            FhirPathEvaluator = (expression, element) =>
            {
                // Use synchronous Evaluate method which internally uses async with timeout
                return fhirPathEvaluatorWithTimeout.Evaluate(expression, element);
            }
        };

        // Set source and target elements
        context.SetSource("src", sourceElement);
        context.SetTarget("tgt", targetElement);

        // Store target ResourceJsonNode for mutation
        context.SetTargetResource("tgt", target);

        logger.LogDebug("Set source element: {SourceType}", sourceElement.InstanceType);

        // Step 8: Execute transformation
        logger.LogInformation(
            "Executing transformation from {SourceType} to {TargetType} using map {MapUrl}",
            request.Content.ResourceType,
            targetType,
            map.Url);

        // Create mutator for resource mutations (schema provider factory)
        var schemaProvider = _currentSchema ?? throw new InvalidOperationException("Schema not initialized");
        var mutator = new JsonNodeMutator(
            fhirPathEvaluator,
            fhirPathParser,
            () => schemaProvider);

        var evaluator = new MappingEvaluator(
            MappingEvaluatorOptions.Default,
            mutator);

        try
        {
            evaluator.Execute(map, context);

            logger.LogInformation(
                "Successfully transformed {SourceType}/{SourceId} to {TargetType}",
                request.Content.ResourceType,
                request.Content.Id,
                targetType);

            return target;
        }
        catch (MappingExecutionException ex)
        {
            logger.LogError(
                ex,
                "Mapping execution failed: {Message} (Location: {Location}, Code: {Code})",
                ex.Message,
                ex.Location,
                ex.Code);

            throw new InvalidOperationException(
                $"StructureMap transformation failed: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Resolves the StructureMap to use for transformation.
    /// Priority: SrcMaps (FML text) → SourceMap (inline resource) → Source (canonical URL).
    /// </summary>
    private async Task<MapExpression> ResolveMapAsync(
        TransformResourceCommand request,
        CancellationToken cancellationToken)
    {
        // Priority 1: FML text (R6+ srcMap parameter)
        if (request.SrcMaps?.Count > 0)
        {
            logger.LogDebug("Parsing StructureMap from FML text");

            try
            {
                return mappingParser.Parse(request.SrcMaps[0]);
            }
            catch (ParseException ex)
            {
                logger.LogError(ex, "Failed to parse FML text: {Message}", ex.Message);
                throw new InvalidOperationException($"Invalid FML text: {ex.Message}", ex);
            }
        }

        // Priority 2: Inline StructureMap resource (sourceMap parameter)
        if (request.SourceMap != null)
        {
            logger.LogDebug(
                "Parsing inline StructureMap resource: {Url}",
                request.SourceMap.Url);

            try
            {
                return structureMapParser.Parse(request.SourceMap);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "Failed to parse inline StructureMap: {Message}", ex.Message);
                throw new InvalidOperationException($"Invalid StructureMap resource: {ex.Message}", ex);
            }
        }

        // Priority 3: Canonical URL (source parameter)
        if (!string.IsNullOrEmpty(request.Source))
        {
            // Validate canonical URL format before calling repository
            if (!Uri.TryCreate(request.Source, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException(
                    $"Invalid canonical URL format: {request.Source}. Must be an absolute URL (e.g., http://example.org/StructureMap/my-map)");
            }

            // Canonical URLs should use http or https scheme
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    $"Invalid canonical URL scheme: {request.Source}. Canonical URLs must use http or https scheme.");
            }

            logger.LogDebug("Resolving StructureMap by canonical URL: {Url}", request.Source);

            // Use cache-aware GetOrLoadAsync (handles cache check, load, parse, and caching)
            return await mapCache.GetOrLoadAsync(request.Source, cancellationToken);
        }

        throw new InvalidOperationException(
            "No mapping source provided. Specify one of: source (canonical URL), sourceMap (inline resource), or srcMap (FML text)");
    }

    /// <summary>
    /// Registers supporting maps in the MapRegistryCache for import resolution.
    /// </summary>
    private void RegisterSupportingMaps(IReadOnlyList<StructureMapJsonNode> supportingMaps)
    {
        logger.LogDebug("Registering {Count} supporting maps", supportingMaps.Count);

        foreach (var supportingMap in supportingMaps)
        {
            try
            {
                var map = structureMapParser.Parse(supportingMap);

                // Only register if not already cached
                if (!mapCache.Contains(map.Url))
                {
                    mapCache.Register(map);
                    logger.LogDebug("Registered supporting map: {Url}", map.Url);
                }
                else
                {
                    logger.LogDebug("Supporting map already registered: {Url}", map.Url);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to parse supporting map {Url}: {Message}",
                    supportingMap.Url,
                    ex.Message);

                // Continue with other supporting maps
            }
        }
    }

    /// <summary>
    /// Resource creator callback for create() transform function.
    /// Creates new FHIR resource instances of the specified type as IElement.
    /// </summary>
    private IElement CreateResourceElement(string resourceType)
    {
        try
        {
            logger.LogDebug("Creating new resource element: {ResourceType}", resourceType);

            var resource = CreateResource(resourceType);
            return resource.ToElement(_currentSchema ?? throw new InvalidOperationException("Schema not initialized"));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to create resource element of type '{ResourceType}': {Message}",
                resourceType,
                ex.Message);

            throw new InvalidOperationException(
                $"Cannot create resource of type '{resourceType}': {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Creates a new FHIR resource of the specified type.
    /// </summary>
    private ResourceJsonNode CreateResource(string resourceType)
    {
        // Create a minimal JSON structure for the resource
        var jsonObject = new JsonObject
        {
            ["resourceType"] = resourceType
        };

        // Use the factory to create the appropriate typed node
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(jsonObject.ToJsonString());
    }

    /// <summary>
    /// Determines the target resource type from the map's uses declarations.
    /// Falls back to source type if no target is explicitly declared.
    /// </summary>
    private string DetermineTargetType(MapExpression map, string sourceType)
    {
        // Look for a uses declaration with mode "target"
        var targetUses = map.Uses.FirstOrDefault(u => u.Mode == ModelMode.Target);

        if (targetUses != null)
        {
            // Extract resource type from StructureDefinition URL
            // e.g., "http://hl7.org/fhir/StructureDefinition/Bundle" → "Bundle"
            var url = targetUses.Url;
            var lastSlash = url.LastIndexOf('/');

            if (lastSlash >= 0 && lastSlash < url.Length - 1)
            {
                var resourceType = url.Substring(lastSlash + 1);

                logger.LogDebug(
                    "Determined target type from uses declaration: {TargetType} (URL: {Url})",
                    resourceType,
                    url);

                return resourceType;
            }
        }

        // Fallback: Use source type (in-place transformation)
        logger.LogDebug(
            "No explicit target uses declaration found, using source type: {SourceType}",
            sourceType);

        return sourceType;
    }
}
