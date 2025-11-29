/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Terminology;

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
