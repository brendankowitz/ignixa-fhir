// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using GreenDonut;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Ignixa.Application.Features.Resource;
using Medino;

namespace Ignixa.Application.Features.Experimental.GraphQl.DataLoaders;

public class ResourceDataLoader(
    IMediator mediator,
    IBatchScheduler batchScheduler,
    DataLoaderOptions options)
    : BatchDataLoader<ResourceKey, JsonElement?>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<ResourceKey, JsonElement?>> LoadBatchAsync(
        IReadOnlyList<ResourceKey> keys,
        CancellationToken cancellationToken)
    {
        var tasks = keys.Select(key => LoadKeyAsync(key, cancellationToken));

        var entries = await Task.WhenAll(tasks).ConfigureAwait(false);

        var results = new Dictionary<ResourceKey, JsonElement?>(keys.Count);
        foreach (var (key, json) in entries)
            results[key] = json;

        return results;
    }

    private async Task<(ResourceKey Key, JsonElement? Json)> LoadKeyAsync(
        ResourceKey key,
        CancellationToken cancellationToken)
    {
        var query = new GetResourceQuery(key.ResourceType, key.ResourceId);
        var entry = await mediator.SendAsync(query, cancellationToken).ConfigureAwait(false);

        if (entry is not null && !entry.IsDeleted)
        {
            var json = FieldResolver.ParseResourceBytes(entry.ResourceBytes);
            return (key, json);
        }

        return (key, null);
    }
}
