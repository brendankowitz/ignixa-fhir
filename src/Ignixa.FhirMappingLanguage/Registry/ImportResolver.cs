/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Import resolver for FHIR Mapping Language.
 */

using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;

namespace Ignixa.FhirMappingLanguage.Registry;

/// <summary>
/// Resolves imports in FHIR mapping definitions.
/// Handles loading imported maps and detecting circular dependencies.
/// </summary>
internal class ImportResolver
{
    private readonly IMapRegistry _registry;
    private readonly IMapLoader? _loader;
    private readonly MappingParser _parser;

    public ImportResolver(IMapRegistry registry, MappingParser parser, IMapLoader? loader = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(parser);

        _registry = registry;
        _parser = parser;
        _loader = loader;
    }

    /// <summary>
    /// Resolves all imports for a map, loading them recursively.
    /// </summary>
    /// <param name="map">The map whose imports should be resolved</param>
    /// <returns>Task that completes when all imports are resolved</returns>
    /// <exception cref="InvalidOperationException">Thrown if circular imports are detected or loading fails</exception>
    public async Task ResolveImportsAsync(MapExpression map)
    {
        var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await ResolveImportsRecursiveAsync(map, visitedUrls);
    }

    /// <summary>
    /// Gets a group from an imported map by URL and group name.
    /// </summary>
    /// <param name="mapUrl">The URL of the imported map</param>
    /// <param name="groupName">The name of the group to retrieve</param>
    /// <returns>The group expression, or null if not found</returns>
    public GroupExpression? GetImportedGroup(string mapUrl, string groupName)
    {
        var importedMap = _registry.GetByUrl(mapUrl);
        if (importedMap is null)
        {
            return null;
        }

        return importedMap.Groups.FirstOrDefault(g =>
            g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds a group by name, searching the current map and all imports.
    /// </summary>
    /// <param name="map">The current map</param>
    /// <param name="groupName">The name of the group to find</param>
    /// <returns>The group expression, or null if not found</returns>
    public GroupExpression? FindGroup(MapExpression map, string groupName)
    {
        // First check the current map
        var group = map.Groups.FirstOrDefault(g =>
            g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));

        if (group is not null)
        {
            return group;
        }

        // Then check imports
        foreach (var import in map.Imports)
        {
            group = GetImportedGroup(import.Url, groupName);
            if (group is not null)
            {
                return group;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets an imported map by its URL.
    /// </summary>
    /// <param name="url">The URL of the map</param>
    /// <returns>The map expression, or null if not found</returns>
    public MapExpression? GetImportedMap(string url)
    {
        return _registry.GetByUrl(url);
    }

    private async Task ResolveImportsRecursiveAsync(MapExpression map, HashSet<string> visitedUrls)
    {
        // Check for circular imports
        if (!visitedUrls.Add(map.Url))
        {
            throw new InvalidOperationException(
                $"Circular import detected: map '{map.Url}' is already being imported");
        }

        // Register the current map if not already registered
        if (!_registry.Contains(map.Url))
        {
            _registry.Register(map);
        }

        // Resolve each import
        foreach (var import in map.Imports)
        {
            // Skip if already resolved
            if (_registry.Contains(import.Url))
            {
                // Check for circular imports even if already in registry
                if (visitedUrls.Contains(import.Url))
                {
                    throw new InvalidOperationException(
                        $"Circular import detected: map '{import.Url}' is already in the import chain");
                }
                continue;
            }

            // Load the imported map
            var importedMap = await LoadMapAsync(import.Url);
            if (importedMap is null)
            {
                throw new InvalidOperationException(
                    $"Failed to load imported map '{import.Url}'");
            }

            // Recursively resolve imports in the loaded map
            await ResolveImportsRecursiveAsync(importedMap, visitedUrls);
        }

        // Remove from visited set when done (allows same map in different branches)
        visitedUrls.Remove(map.Url);
    }

    private async Task<MapExpression?> LoadMapAsync(string url)
    {
        if (_loader is null)
        {
            throw new InvalidOperationException(
                $"Cannot load map '{url}': no map loader configured");
        }

        if (!_loader.CanLoad(url))
        {
            return null;
        }

        var content = await _loader.LoadAsync(url);
        if (content is null)
        {
            return null;
        }

        // Parse the loaded map
        return _parser.Parse(content);
    }
}
