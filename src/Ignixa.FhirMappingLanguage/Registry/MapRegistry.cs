/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * In-memory map registry implementation.
 */

using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.FhirMappingLanguage.Registry;

/// <summary>
/// In-memory implementation of IMapRegistry.
/// Stores maps in a dictionary keyed by URL.
/// </summary>
public class MapRegistry : IMapRegistry
{
    private readonly Dictionary<string, MapExpression> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void Register(MapExpression map)
    {
        ArgumentNullException.ThrowIfNull(map);

        lock (_lock)
        {
            if (_maps.ContainsKey(map.Url))
            {
                throw new InvalidOperationException(
                    $"A map with URL '{map.Url}' is already registered");
            }

            _maps[map.Url] = map;
        }
    }

    public MapExpression? GetByUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        lock (_lock)
        {
            return _maps.TryGetValue(url, out var map) ? map : null;
        }
    }

    public bool Contains(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        lock (_lock)
        {
            return _maps.ContainsKey(url);
        }
    }

    public IEnumerable<string> GetAllUrls()
    {
        lock (_lock)
        {
            return _maps.Keys.ToList();
        }
    }

    public bool Unregister(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        lock (_lock)
        {
            return _maps.Remove(url);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _maps.Clear();
        }
    }
}
