// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Application.Features.Resource;
using Ignixa.Serialization.Abstractions;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.GraphQl.Resolvers;

public sealed class ResourceResolver(IMediator mediator, ILogger<ResourceResolver> logger)
{
    public async Task<JsonElement?> ResolveByIdAsync(
        string resourceType,
        string id,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL resolving {ResourceType}/{Id}", resourceType, id);

        try
        {
            var result = await mediator.SendAsync(new GetResourceQuery(resourceType, id), cancellationToken);

            if (result is null || result.IsDeleted)
                return null;

            return FieldResolver.ParseResourceBytes(result.ResourceBytes);
        }
        catch (OperationCanceledException) { throw; }
        catch (FhirException ex) { throw FhirGraphQlErrorMapping.Map(ex, $"Read {resourceType}/{id}", logger); }
    }
}

