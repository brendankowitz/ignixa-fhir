/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Map registry interface for FHIR Mapping Language.
 */

using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.FhirMappingLanguage.Registry;

/// <summary>
/// Registry for storing and retrieving FHIR mapping definitions.
/// Used to resolve imported maps during evaluation.
/// </summary>
public interface IMapRegistry
{
    /// <summary>
    /// Registers a map in the registry.
    /// </summary>
    /// <param name="map">The map expression to register</param>
    /// <exception cref="InvalidOperationException">Thrown if a map with the same URL already exists</exception>
    void Register(MapExpression map);

    /// <summary>
    /// Gets a map by its URL.
    /// </summary>
    /// <param name="url">The URL of the map to retrieve</param>
    /// <returns>The map expression, or null if not found</returns>
    MapExpression? GetByUrl(string url);

    /// <summary>
    /// Checks if a map with the specified URL exists in the registry.
    /// </summary>
    /// <param name="url">The URL to check</param>
    /// <returns>True if the map exists, false otherwise</returns>
    bool Contains(string url);

    /// <summary>
    /// Gets all registered map URLs.
    /// </summary>
    /// <returns>Collection of all registered map URLs</returns>
    IEnumerable<string> GetAllUrls();

    /// <summary>
    /// Removes a map from the registry.
    /// </summary>
    /// <param name="url">The URL of the map to remove</param>
    /// <returns>True if the map was removed, false if it didn't exist</returns>
    bool Unregister(string url);

    /// <summary>
    /// Clears all maps from the registry.
    /// </summary>
    void Clear();
}

/// <summary>
/// Loader interface for resolving map URLs to map content.
/// Implementations can load from files, databases, HTTP, etc.
/// </summary>
public interface IMapLoader
{
    /// <summary>
    /// Loads a map from the specified URL.
    /// </summary>
    /// <param name="url">The URL of the map to load</param>
    /// <returns>The map text content, or null if not found</returns>
    /// <exception cref="InvalidOperationException">Thrown if loading fails</exception>
    Task<string?> LoadAsync(string url);

    /// <summary>
    /// Checks if the loader can handle the specified URL.
    /// </summary>
    /// <param name="url">The URL to check</param>
    /// <returns>True if this loader can load the URL, false otherwise</returns>
    bool CanLoad(string url);
}
