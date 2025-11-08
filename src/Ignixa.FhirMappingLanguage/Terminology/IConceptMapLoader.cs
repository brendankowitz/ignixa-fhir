/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * ConceptMap loader interface for FHIR Mapping Language.
 */

namespace Ignixa.FhirMappingLanguage.Terminology;

/// <summary>
/// Interface for loading ConceptMap resources.
/// </summary>
public interface IConceptMapLoader
{
    /// <summary>
    /// Loads a ConceptMap by its canonical URL.
    /// </summary>
    /// <param name="url">The canonical URL of the ConceptMap</param>
    /// <returns>The ConceptMap content as JSON string, or null if not found</returns>
    Task<string?> LoadAsync(string url);

    /// <summary>
    /// Checks if this loader can load the specified ConceptMap.
    /// </summary>
    /// <param name="url">The canonical URL of the ConceptMap</param>
    /// <returns>True if the loader can load this ConceptMap, false otherwise</returns>
    bool CanLoad(string url);
}

/// <summary>
/// Dictionary-based ConceptMap loader for testing and simple scenarios.
/// Stores ConceptMaps in memory.
/// </summary>
public class DictionaryConceptMapLoader : IConceptMapLoader
{
    private readonly Dictionary<string, string> _conceptMaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a ConceptMap to the loader.
    /// </summary>
    /// <param name="url">The canonical URL of the ConceptMap</param>
    /// <param name="content">The ConceptMap content as JSON string</param>
    public void AddConceptMap(string url, string content)
    {
        _conceptMaps[url] = content;
    }

    public Task<string?> LoadAsync(string url)
    {
        return Task.FromResult(_conceptMaps.TryGetValue(url, out var content) ? content : null);
    }

    public bool CanLoad(string url)
    {
        return _conceptMaps.ContainsKey(url);
    }
}

/// <summary>
/// Composite ConceptMap loader that chains multiple loaders.
/// Tries each loader in order until one succeeds.
/// </summary>
public class CompositeConceptMapLoader : IConceptMapLoader
{
    private readonly List<IConceptMapLoader> _loaders = new();

    /// <summary>
    /// Adds a loader to the chain.
    /// </summary>
    /// <param name="loader">The loader to add</param>
    public void AddLoader(IConceptMapLoader loader)
    {
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
        return _loaders.Any(l => l.CanLoad(url));
    }
}
