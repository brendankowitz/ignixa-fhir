// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Exceptions;
using Ignixa.Serialization.Abstractions;
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

        var jsonNode = ParseResourceJson(resourceJson);

        try
        {
            var id = Guid.NewGuid().ToString("N");
            var command = new CreateOrUpdateResourceCommand(resourceType, id, jsonNode, System.Net.Http.HttpMethod.Post);
            var result = await mediator.SendAsync(command, cancellationToken);
            return result?.ResourceBytes.Length > 0
                ? FieldResolver.ParseResourceBytes(result.ResourceBytes) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (FhirException ex) { throw MapFhirException(ex, $"Create {resourceType}"); }
    }

    public async Task<JsonElement?> UpdateAsync(
        string resourceType, string id, string resourceJson, CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL updating {ResourceType}/{Id}", resourceType, id);

        var jsonNode = ParseResourceJson(resourceJson);

        try
        {
            var command = new CreateOrUpdateResourceCommand(resourceType, id, jsonNode, System.Net.Http.HttpMethod.Put);
            var result = await mediator.SendAsync(command, cancellationToken);
            return result?.ResourceBytes.Length > 0
                ? FieldResolver.ParseResourceBytes(result.ResourceBytes) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (FhirException ex) { throw MapFhirException(ex, $"Update {resourceType}/{id}"); }
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
        catch (FhirException ex) { throw MapFhirException(ex, $"Delete {resourceType}/{id}"); }
    }

    private static ResourceJsonNode ParseResourceJson(string resourceJson)
    {
        try { return ResourceJsonNode.Parse(resourceJson); }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        { throw CreateError("Invalid resource JSON", "INVALID_RESOURCE", ex); }
    }

    private GraphQLException MapFhirException(FhirException exception, string operation)
    {
        var code = exception is ResourceNotFoundException ? "FHIR_NOT_FOUND" : CodeFromStatus(exception.StatusCode);
        logger.LogWarning(exception, "GraphQL mutation {Operation} failed with status {StatusCode} ({Code})",
            operation, exception.StatusCode, code);

        return new GraphQLException(
            ErrorBuilder.New()
                .SetMessage(exception.Message)
                .SetCode(code)
                .SetException(exception)
                .Build());
    }

    private static string CodeFromStatus(int statusCode) => statusCode switch
    {
        404 => "FHIR_NOT_FOUND",
        409 or 412 => "FHIR_VERSION_CONFLICT",
        400 => "INVALID_RESOURCE",
        _ => "FHIR_OPERATION_FAILED",
    };

    private static GraphQLException CreateError(string message, string code, Exception inner) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode(code).SetException(inner).Build());
}
