/* Copyright (c) 2025, Ignixa Contributors */

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
