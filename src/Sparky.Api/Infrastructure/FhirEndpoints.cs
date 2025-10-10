// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;
using Sparky.Application.Features.Bundle;
using Sparky.Application.Features.Bundle.Serialization;
using Sparky.Application.Features.Resource;
using Sparky.Domain.Models;
using Sparky.Search.Parsing;
using Sparky.SourceNodeSerialization;
using FhirBundle = Hl7.Fhir.Model.Bundle;
using DeferredWriteCoordinator = Sparky.Application.Features.Bundle.DeferredWriteCoordinator;

namespace Sparky.Api.Infrastructure;

/// <summary>
/// Registers FHIR RESTful endpoints for all resource types.
/// No controllers, no switch statements - pure endpoint routing.
/// </summary>
public static class FhirEndpoints
{
    private const string _contentTypeApplicationFhirJson = "application/fhir+json";
    private const string _contentTypeApplicationJson = "application/json";

    /// <summary>
    /// Registers FHIR RESTful endpoints for all resource types.
    /// </summary>
    public static IEndpointRouteBuilder MapFhirEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /{resourceType}/{id} - Read resource
        endpoints.MapGet("/{resourceType}/{id}", HandleGetResource)
            .WithName("GetResource")
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status404NotFound);

        // PUT /{resourceType}/{id} - Create or update resource
        endpoints.MapPut("/{resourceType}/{id}", HandlePutResource)
            .WithName("PutResource")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status201Created);

        // DELETE /{resourceType}/{id} - Delete resource
        endpoints.MapDelete("/{resourceType}/{id}", HandleDeleteResource)
            .WithName("DeleteResource")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        // GET /{resourceType} - Search resources
        endpoints.MapGet("/{resourceType}", HandleSearchResource)
            .WithName("SearchResource")
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson, _contentTypeApplicationJson);

        // POST /{resourceType} - Create resource (server assigns ID)
        endpoints.MapPost("/{resourceType}", HandlePostResource)
            .WithName("PostResource")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces(StatusCodes.Status201Created);

        // POST / - Transaction/Batch bundle
        endpoints.MapPost("/", HandleBundle)
            .WithName("Bundle")
            .Accepts<object>(_contentTypeApplicationFhirJson, _contentTypeApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, _contentTypeApplicationFhirJson)
            .Produces(StatusCodes.Status501NotImplemented);

        return endpoints;
    }

    /// <summary>
    /// GET /{resourceType}/{id}
    /// </summary>
    private static async Task<IResult> HandleGetResource(
        HttpContext context,
        [FromRoute] string resourceType,
        [FromRoute] string id,
        [FromServices] IMediator mediator,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("GET /{ResourceType}/{Id}", resourceType, id);

        // Validate resource type exists
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.NotFound(new { error = $"Resource type '{resourceType}' not supported" });
        }

        // Send generic query
        var query = new GetResourceQuery(resourceType, id);
        ResourceWrapper? result = await mediator.SendAsync(query, ct);

        if (result == null)
        {
            logger.LogInformation("Resource {ResourceType}/{Id} not found", resourceType, id);
            return Results.NotFound();
        }

        // Add headers
        context.Response.Headers.Append("ETag", $"W/\"{result.VersionId}\"");
        context.Response.Headers.Append("Last-Modified", result.LastModified.ToString("R"));

        // Return raw JSON
        return Results.Content(result.RawJson ?? "{}", _contentTypeApplicationFhirJson);
    }

    /// <summary>
    /// PUT /{resourceType}/{id}
    /// </summary>
    private static async Task<IResult> HandlePutResource(
        HttpContext context,
        [FromRoute] string resourceType,
        [FromRoute] string id,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("PUT /{ResourceType}/{Id}", resourceType, id);

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.BadRequest(new { error = $"Resource type '{resourceType}' not supported" });
        }

        // Read request body
        string json;
        using (var memoryStream = memoryStreamManager.GetStream("request-body"))
        {
            await context.Request.Body.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream, Encoding.UTF8);
            json = await reader.ReadToEndAsync(ct);
        }

        // Parse JSON to ISourceNode
        var sourceNode = JsonSourceNodeFactory.Parse(json);

        // Validate resource type matches
        if (!string.Equals(sourceNode.Name, resourceType, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Resource type mismatch: expected '{ExpectedType}', got '{ActualType}'",
                resourceType,
                sourceNode.Name);
            return Results.BadRequest(new { error = $"Resource type must be '{resourceType}', got '{sourceNode.Name}'" });
        }

        // Extract deferred write coordinator from HttpContext if in bundle context
        var coordinator = context.Items.TryGetValue("DeferredWriteCoordinator", out var coordinatorObj)
            ? coordinatorObj as DeferredWriteCoordinator
            : null;

        // Send generic command with optional coordinator
        var command = new CreateOrUpdateResourceCommand(resourceType, id, sourceNode, json, coordinator);
        ResourceKey result = await mediator.SendAsync(command, ct);

        // Add ETag header
        context.Response.Headers.Append("ETag", $"W/\"{result.VersionId}\"");

        // Determine if created or updated
        bool isCreated = result.VersionId == "1";

        if (isCreated)
        {
            logger.LogInformation("Created {ResourceType}/{Id} (version {Version})", resourceType, result.Id, result.VersionId);
            return Results.Created($"/{resourceType}/{result.Id}", new
            {
                resourceType = resourceType,
                id = result.Id,
                meta = new { versionId = result.VersionId }
            });
        }

        logger.LogInformation("Updated {ResourceType}/{Id} (version {Version})", resourceType, result.Id, result.VersionId);
        return Results.Ok(new
        {
            resourceType = resourceType,
            id = result.Id,
            meta = new { versionId = result.VersionId }
        });
    }

    /// <summary>
    /// GET /{resourceType} - Search
    /// </summary>
    private static async Task<IResult> HandleSearchResource(
        HttpContext context,
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] IQueryParameterParser queryParser,
        [FromServices] ISearchOptionsBuilder searchOptionsBuilder,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("GET /{ResourceType}?{QueryString}", resourceType, context.Request.QueryString);

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.NotFound(new { error = $"Resource type '{resourceType}' not supported" });
        }

        // Parse query parameters
        var queryParameters = queryParser.Parse(context.Request.Query);

        // Build SearchOptions
        var searchOptions = searchOptionsBuilder.Build(resourceType, queryParameters);

        // Send search query
        var searchQuery = new SearchResourcesQuery(resourceType, searchOptions);
        SearchResourcesResult result = await mediator.SendAsync(searchQuery, ct);

        // Build self link
        string selfLink = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";

        // Set response headers
        context.Response.ContentType = "application/fhir+json; charset=utf-8";

        // Stream Bundle response
        await StreamingBundleSerializer.SerializeAsync(
            outputStream: context.Response.Body,
            bundleType: "searchset",
            total: result.Total,
            entries: result.Resources,
            selfLink: selfLink,
            nextLink: null,
            pretty: false,
            cancellationToken: ct);

        // Response already written to the body, return empty result
        return Results.Empty;
    }

    /// <summary>
    /// POST /{resourceType} - Create (server assigns ID)
    /// </summary>
    private static async Task<IResult> HandlePostResource(
        HttpContext context,
        [FromRoute] string resourceType,
        [FromServices] IMediator mediator,
        [FromServices] RecyclableMemoryStreamManager memoryStreamManager,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("POST /{ResourceType}", resourceType);

        // Generate ID
        string id = Guid.NewGuid().ToString("N");

        logger.LogInformation("Generated ID {Id} for new {ResourceType}", id, resourceType);

        // Delegate to PUT handler logic
        return await HandlePutResource(context, resourceType, id, mediator, memoryStreamManager, logger, ct);
    }

    /// <summary>
    /// DELETE /{resourceType}/{id}
    /// </summary>
    private static async Task<IResult> HandleDeleteResource(
        HttpContext context,
        [FromRoute] string resourceType,
        [FromRoute] string id,
        [FromServices] IMediator mediator,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("DELETE /{ResourceType}/{Id}", resourceType, id);

        // Validate resource type
        if (!IsValidResourceType(resourceType, context))
        {
            logger.LogWarning("Resource type '{ResourceType}' not supported", resourceType);
            return Results.NotFound(new { error = $"Resource type '{resourceType}' not supported" });
        }

        // Send delete command
        var command = new DeleteResourceCommand(resourceType, id);
        bool deleted = await mediator.SendAsync(command, ct);

        if (!deleted)
        {
            logger.LogInformation("Resource {ResourceType}/{Id} not found for deletion", resourceType, id);
            return Results.NotFound();
        }

        logger.LogInformation("Deleted {ResourceType}/{Id}", resourceType, id);
        return Results.NoContent();
    }

    /// <summary>
    /// POST / - Transaction/Batch bundle
    /// Always uses streaming parser, buffers when needed for urn:uuid resolution.
    /// Phase 2: Supports true end-to-end streaming for batch bundles without urn:uuid.
    /// </summary>
    private static async Task<IResult> HandleBundle(
        HttpContext context,
        [FromServices] BundleProcessor bundleProcessor,
        [FromServices] StreamingBundleParser streamingParser,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation("POST / (Bundle)");

        // ALWAYS parse with streaming parser - returns metadata + streaming entries
        var bundleContext = await streamingParser.ParseStreamAsync(context.Request.Body, ct);

        // Validate resource type
        if (bundleContext.ResourceType != "Bundle")
        {
            logger.LogWarning("Expected Bundle resource, got '{ResourceType}'", bundleContext.ResourceType);
            return Results.BadRequest(new { error = $"Expected Bundle resource, got '{bundleContext.ResourceType}'" });
        }

        // Log parsing issues
        foreach (var issue in bundleContext.ParsingIssues)
        {
            logger.LogWarning("Bundle parsing issue: {Issue}", issue);
        }

        // Determine bundle type (default to Batch if not specified)
        var bundleTypeString = bundleContext.BundleType?.ToUpperInvariant();
        var bundleType = bundleTypeString switch
        {
            "TRANSACTION" => BundleType.Transaction,
            "BATCH" => BundleType.Batch,
            _ => BundleType.Batch // Default to batch
        };

        logger.LogDebug("Bundle type: {BundleType}", bundleType);

        var options = new BundleProcessingOptions
        {
            MaxParallelism = 10,
            ChannelCapacity = 100,
            Type = bundleType
        };

        // Phase 2: Dual-mode routing
        if (options.Type == BundleType.Transaction)
        {
            logger.LogInformation("Using buffered processing (Transaction: {IsTransaction})",
                options.Type == BundleType.Transaction);

            FhirBundle responseBundle = await bundleProcessor.ProcessAsync(
                bundleContext.Entries, options, ct);

            // Serialize response bundle
            string responseJson;
            try
            {
                var serializer = new FhirJsonSerializer();
                responseJson = serializer.SerializeToString(responseBundle);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to serialize response bundle");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            logger.LogInformation("Successfully processed bundle (buffered mode)");
            return Results.Content(responseJson, _contentTypeApplicationFhirJson);
        }
        else
        {
            // STREAMING MODE: Batch bundle with no urn:uuid references
            // True end-to-end streaming - responses written as they complete
            logger.LogInformation("Using streaming processing (Batch bundle, no urn:uuid references)");

            try
            {
                // Get streaming context
                var streamingContext = await bundleProcessor.ProcessBatchStreamingAsync(
                    bundleContext.Entries, options, ct);

                // Set response content type
                context.Response.ContentType = "application/fhir+json; charset=utf-8";

                // Stream responses directly to HTTP
                await StreamingBundleSerializer.SerializeStreamAsync(
                    outputStream: context.Response.Body,
                    bundleType: "batch-response",
                    entryResponses: streamingContext.ResponseStream,
                    total: null,
                    selfLink: null,
                    nextLink: null,
                    pretty: false,
                    cancellationToken: ct);

                // Complete background tasks
                await streamingContext.CompleteAsync();

                logger.LogInformation("Successfully processed bundle (streaming mode)");

                // Response already written to stream
                return Results.Empty;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process streaming bundle");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }

    /// <summary>
    /// Validates resource type against capability statement or schema provider.
    /// For now, returns true for all resource types (will implement proper validation later).
    /// </summary>
    private static bool IsValidResourceType(string resourceType, HttpContext context)
    {
        // TODO: Implement proper validation using IFhirSchemaProvider or ICapabilityStatementService
        // For now, accept all resource types to support dynamic routing
        return true;
    }

    /// <summary>
    /// Converts a list to an async enumerable.
    /// </summary>
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (T item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await System.Threading.Tasks.Task.Yield(); // Allow cooperative multitasking
        }
    }
}
