// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate;
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
    public async Task<JsonElement?> CreateAsync(
        string resourceType, string resourceJson, CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL creating {ResourceType}", resourceType);

        ResourceJsonNode jsonNode;
        try { jsonNode = ResourceJsonNode.Parse(resourceJson); }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        { throw CreateError("Invalid resource JSON", "INVALID_RESOURCE", ex); }

        try
        {
            var id = Guid.NewGuid().ToString("N");
            var command = new CreateOrUpdateResourceCommand(resourceType, id, jsonNode, System.Net.Http.HttpMethod.Post);
            var result = await mediator.SendAsync(command, cancellationToken);
            return result?.ResourceBytes.Length > 0
                ? JsonSerializer.Deserialize<JsonElement>(result.ResourceBytes.Span) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw CreateError($"Create {resourceType} failed: {ex.Message}", "MUTATION_FAILED", ex); }
    }

    public async Task<JsonElement?> UpdateAsync(
        string resourceType, string id, string resourceJson, CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL updating {ResourceType}/{Id}", resourceType, id);

        ResourceJsonNode jsonNode;
        try { jsonNode = ResourceJsonNode.Parse(resourceJson); }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        { throw CreateError("Invalid resource JSON", "INVALID_RESOURCE", ex); }

        try
        {
            var command = new CreateOrUpdateResourceCommand(resourceType, id, jsonNode, System.Net.Http.HttpMethod.Put);
            var result = await mediator.SendAsync(command, cancellationToken);
            return result?.ResourceBytes.Length > 0
                ? JsonSerializer.Deserialize<JsonElement>(result.ResourceBytes.Span) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw CreateError($"Update {resourceType}/{id} failed: {ex.Message}", "MUTATION_FAILED", ex); }
    }

    public async Task<bool> DeleteAsync(
        string resourceType, string id, CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL deleting {ResourceType}/{Id}", resourceType, id);
        try
        {
            var command = new DeleteResourceCommand(resourceType, id);
            return await mediator.SendAsync(command, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw CreateError($"Delete {resourceType}/{id} failed: {ex.Message}", "MUTATION_FAILED", ex); }
    }

    private static GraphQLException CreateError(string message, string code, Exception inner) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode(code).SetException(inner).Build());
}
