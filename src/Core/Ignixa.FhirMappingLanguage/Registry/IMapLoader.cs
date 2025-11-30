/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Registry;

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
