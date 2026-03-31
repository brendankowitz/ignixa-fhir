// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Application.Features.Resource;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.GraphQl.Resolvers;

/// <summary>
/// Resolves GraphQL mutations by delegating to CQRS commands for
/// creating, updating, and deleting FHIR resources.
/// </summary>
public sealed class MutationResolver(IMediator mediator, ILogger<MutationResolver> logger)
{
    /// <summary>
    /// Creates a new FHIR resource from its JSON representation.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient").</param>
    /// <param name="resourceJson">The JSON string of the resource to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created resource as a <see cref="JsonElement"/>, or null if the result is empty.</returns>
    public async Task<JsonElement?> CreateAsync(
        string resourceType,
        string resourceJson,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL creating {ResourceType}", resourceType);

        var jsonNode = ResourceJsonNode.Parse(resourceJson);
        var id = Guid.NewGuid().ToString("N");

        var command = new CreateOrUpdateResourceCommand(
            resourceType,
            id,
            jsonNode,
            System.Net.Http.HttpMethod.Post);

        var result = await mediator.SendAsync(command, cancellationToken);

        if (result?.ResourceBytes.Length > 0)
        {
            return JsonSerializer.Deserialize<JsonElement>(result.ResourceBytes.Span);
        }

        return null;
    }

    /// <summary>
    /// Updates an existing FHIR resource with the given JSON representation.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient").</param>
    /// <param name="id">The resource ID to update.</param>
    /// <param name="resourceJson">The JSON string of the updated resource.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated resource as a <see cref="JsonElement"/>, or null if the result is empty.</returns>
    public async Task<JsonElement?> UpdateAsync(
        string resourceType,
        string id,
        string resourceJson,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL updating {ResourceType}/{Id}", resourceType, id);

        var jsonNode = ResourceJsonNode.Parse(resourceJson);

        var command = new CreateOrUpdateResourceCommand(
            resourceType,
            id,
            jsonNode,
            System.Net.Http.HttpMethod.Put);

        var result = await mediator.SendAsync(command, cancellationToken);

        if (result?.ResourceBytes.Length > 0)
        {
            return JsonSerializer.Deserialize<JsonElement>(result.ResourceBytes.Span);
        }

        return null;
    }

    /// <summary>
    /// Deletes a FHIR resource by type and ID.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient").</param>
    /// <param name="id">The resource ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the resource was deleted successfully.</returns>
    public async Task<bool> DeleteAsync(
        string resourceType,
        string id,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL deleting {ResourceType}/{Id}", resourceType, id);

        var command = new DeleteResourceCommand(resourceType, id);
        return await mediator.SendAsync(command, cancellationToken);
    }
}
