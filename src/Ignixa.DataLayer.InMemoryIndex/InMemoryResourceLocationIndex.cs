// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.DataLayer.InMemoryIndex;

/// <summary>
/// In-memory implementation of resource location tracking.
/// Uses ConcurrentDictionary for thread-safe operations.
/// </summary>
/// <remarks>
/// This is a prototype implementation. Production would use:
/// - Persistent storage (Redis, SQL, etc.)
/// - Distributed cache for multi-server scenarios
/// - Eviction policies for memory management
/// </remarks>
public class InMemoryResourceLocationIndex : IResourceLocationIndex
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _index = new();
    private readonly object _lock = new();

    public ValueTask AddAsync(ResourceKey key, string dataLayerName, CancellationToken ct = default)
    {
        string indexKey = GetIndexKey(key);

        _index.AddOrUpdate(
            indexKey,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { dataLayerName },
            (_, existingLayers) =>
            {
                lock (_lock)
                {
                    existingLayers.Add(dataLayerName);
                    return existingLayers;
                }
            });

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<string>> GetLayersAsync(ResourceKey key, CancellationToken ct = default)
    {
        string indexKey = GetIndexKey(key);

        if (_index.TryGetValue(indexKey, out HashSet<string>? layers))
        {
            lock (_lock)
            {
                return ValueTask.FromResult<IReadOnlyCollection<string>>(layers.ToArray());
            }
        }

        return ValueTask.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }

    public ValueTask RemoveAsync(ResourceKey key, string dataLayerName, CancellationToken ct = default)
    {
        string indexKey = GetIndexKey(key);

        if (_index.TryGetValue(indexKey, out HashSet<string>? layers))
        {
            lock (_lock)
            {
                layers.Remove(dataLayerName);
                if (layers.Count == 0)
                {
                    _index.TryRemove(indexKey, out _);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<string>> GetAllLayerNamesAsync(CancellationToken ct = default)
    {
        var allLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var layers in _index.Values)
        {
            lock (_lock)
            {
                foreach (var layer in layers)
                {
                    allLayers.Add(layer);
                }
            }
        }

        return ValueTask.FromResult<IReadOnlyCollection<string>>(allLayers.ToArray());
    }

    private static string GetIndexKey(ResourceKey key)
    {
        // Use ResourceType/Id as the key (ignore version for location tracking)
        return $"{key.ResourceType}/{key.Id}";
    }
}
