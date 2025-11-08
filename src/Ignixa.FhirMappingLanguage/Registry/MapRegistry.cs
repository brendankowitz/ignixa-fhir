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

/// <summary>
/// Composite map loader that tries multiple loaders in sequence.
/// </summary>
public class CompositeMapLoader : IMapLoader
{
    private readonly List<IMapLoader> _loaders = new();
    private readonly MappingCompiler _compiler;

    public CompositeMapLoader(MappingCompiler compiler)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    /// <summary>
    /// Adds a loader to the composite.
    /// Loaders are tried in the order they are added.
    /// </summary>
    public void AddLoader(IMapLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        _loaders.Add(loader);
    }

    public async Task<string?> LoadAsync(string url)
    {
        foreach (var loader in _loaders)
        {
            if (loader.CanLoad(url))
            {
                var content = await loader.LoadAsync(url);
                if (content != null)
                {
                    return content;
                }
            }
        }

        return null;
    }

    public bool CanLoad(string url)
    {
        return _loaders.Any(loader => loader.CanLoad(url));
    }
}

/// <summary>
/// Map loader that loads from a dictionary of URL->content mappings.
/// Useful for testing and in-memory scenarios.
/// </summary>
public class DictionaryMapLoader : IMapLoader
{
    private readonly Dictionary<string, string> _maps = new(StringComparer.OrdinalIgnoreCase);

    public void AddMap(string url, string content)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty", nameof(url));
        }

        ArgumentNullException.ThrowIfNull(content);

        _maps[url] = content;
    }

    public Task<string?> LoadAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(_maps.TryGetValue(url, out var content) ? content : null);
    }

    public bool CanLoad(string url)
    {
        return !string.IsNullOrWhiteSpace(url) && _maps.ContainsKey(url);
    }
}
