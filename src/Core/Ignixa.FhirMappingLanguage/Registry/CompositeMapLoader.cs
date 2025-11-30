/* Copyright (c) 2025, Ignixa Contributors */

using Ignixa.FhirMappingLanguage.Parser;

namespace Ignixa.FhirMappingLanguage.Registry;

/// <summary>
/// Composite map loader that tries multiple loaders in sequence.
/// </summary>
public class CompositeMapLoader : IMapLoader
{
    private readonly List<IMapLoader> _loaders = [];
    private readonly MappingParser _parser;

    public CompositeMapLoader(MappingParser parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
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
                if (content is not null)
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
