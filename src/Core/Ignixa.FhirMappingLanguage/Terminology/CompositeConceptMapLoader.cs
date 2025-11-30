/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Terminology;

/// <summary>
/// Composite ConceptMap loader that chains multiple loaders.
/// Tries each loader in order until one succeeds.
/// </summary>
public class CompositeConceptMapLoader : IConceptMapLoader
{
    private readonly List<IConceptMapLoader> _loaders = [];

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
