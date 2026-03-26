// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using GreenDonut;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
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
        var results = new Dictionary<ResourceKey, JsonElement?>(keys.Count);

        var tasks = keys.Select(async key =>
        {
            var query = new GetResourceQuery(key.ResourceType, key.ResourceId);
            var entry = await mediator.SendAsync(query, cancellationToken);

            if (entry is not null && !entry.IsDeleted)
            {
                var json = JsonSerializer.Deserialize<JsonElement>(entry.ResourceBytes.Span);
                return (key, json: (JsonElement?)json);
            }

            return (key, json: (JsonElement?)null);
        });

        foreach (var (key, json) in await Task.WhenAll(tasks))
        {
            results[key] = json;
        }

        return results;
    }
}
