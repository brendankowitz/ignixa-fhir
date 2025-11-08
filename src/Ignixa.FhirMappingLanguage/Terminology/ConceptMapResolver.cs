/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * ConceptMap resolver for FHIR Mapping Language terminology translation.
 */

using System.Text.Json;

namespace Ignixa.FhirMappingLanguage.Terminology;

/// <summary>
/// Resolves terminology translations using FHIR ConceptMap resources.
/// </summary>
public class ConceptMapResolver
{
    private readonly Dictionary<string, JsonDocument> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IConceptMapLoader? _loader;
    private readonly object _lock = new();

    public ConceptMapResolver(IConceptMapLoader? loader = null)
    {
        _loader = loader;
    }

    /// <summary>
    /// Translates a code using the specified ConceptMap.
    /// </summary>
    /// <param name="conceptMapUrl">The canonical URL of the ConceptMap</param>
    /// <param name="sourceCode">The source code to translate</param>
    /// <param name="targetSystem">The target code system (optional)</param>
    /// <returns>The translated code, or null if no translation found</returns>
    public async Task<string?> TranslateAsync(string conceptMapUrl, string sourceCode, string? targetSystem = null)
    {
        var conceptMap = await LoadConceptMapAsync(conceptMapUrl);
        if (conceptMap == null)
        {
            return null;
        }

        // Navigate through the ConceptMap structure
        if (!conceptMap.RootElement.TryGetProperty("group", out var groups))
        {
            return null;
        }

        foreach (var group in groups.EnumerateArray())
        {
            // If target system is specified, filter by target
            if (targetSystem != null)
            {
                if (!group.TryGetProperty("target", out var target) ||
                    target.GetString() != targetSystem)
                {
                    continue;
                }
            }

            // Look through elements for matching source code
            if (!group.TryGetProperty("element", out var elements))
            {
                continue;
            }

            foreach (var element in elements.EnumerateArray())
            {
                if (!element.TryGetProperty("code", out var code) ||
                    code.GetString() != sourceCode)
                {
                    continue;
                }

                // Found matching source code, get first target
                if (!element.TryGetProperty("target", out var targets))
                {
                    continue;
                }

                foreach (var target in targets.EnumerateArray())
                {
                    if (target.TryGetProperty("code", out var targetCode))
                    {
                        return targetCode.GetString();
                    }
                }
            }
        }

        return null; // No translation found
    }

    /// <summary>
    /// Creates a resolver function for use in MappingContext.
    /// </summary>
    /// <returns>A function that performs synchronous translation</returns>
    public Func<string, string, string, string?> CreateResolverFunction()
    {
        return (conceptMapUrl, sourceCode, targetSystem) =>
        {
            // Synchronous wrapper - this could block, but matches the delegate signature
            return TranslateAsync(conceptMapUrl, sourceCode, targetSystem).GetAwaiter().GetResult();
        };
    }

    /// <summary>
    /// Clears the ConceptMap cache.
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            foreach (var doc in _cache.Values)
            {
                doc.Dispose();
            }
            _cache.Clear();
        }
    }

    private async Task<JsonDocument?> LoadConceptMapAsync(string url)
    {
        // Check cache first
        lock (_lock)
        {
            if (_cache.TryGetValue(url, out var cached))
            {
                return cached;
            }
        }

        // Load from loader
        if (_loader == null)
        {
            throw new InvalidOperationException(
                $"Cannot load ConceptMap '{url}': no loader configured");
        }

        if (!_loader.CanLoad(url))
        {
            return null;
        }

        var content = await _loader.LoadAsync(url);
        if (content == null)
        {
            return null;
        }

        // Parse and cache
        var document = JsonDocument.Parse(content);
        lock (_lock)
        {
            if (!_cache.ContainsKey(url))
            {
                _cache[url] = document;
            }
            else
            {
                // Another thread cached it, dispose ours
                document.Dispose();
            }
            return _cache[url];
        }
    }
}
