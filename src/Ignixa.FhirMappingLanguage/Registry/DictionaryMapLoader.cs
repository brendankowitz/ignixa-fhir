/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Registry;

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
