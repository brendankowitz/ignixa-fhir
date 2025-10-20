// <copyright file="CachedValidationSchemaResolver.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Collections.Concurrent;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Caching decorator for IValidationSchemaResolver that uses ConcurrentDictionary for thread-safe caching.
/// Implements the Decorator pattern to wrap any IValidationSchemaResolver implementation.
/// </summary>
public class CachedValidationSchemaResolver : IValidationSchemaResolver
{
    private readonly IValidationSchemaResolver _inner;
    private readonly ConcurrentDictionary<string, ValidationSchema?> _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedValidationSchemaResolver"/> class.
    /// </summary>
    /// <param name="inner">The inner resolver to wrap with caching.</param>
    /// <exception cref="ArgumentNullException">Thrown if inner is null.</exception>
    public CachedValidationSchemaResolver(IValidationSchemaResolver inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = new ConcurrentDictionary<string, ValidationSchema?>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the validation schema for a given canonical URL (e.g., StructureDefinition URL).
    /// Results are cached for subsequent lookups. Null results are also cached to avoid repeated failed lookups.
    /// </summary>
    /// <param name="canonicalUrl">The canonical URL of the schema to retrieve.</param>
    /// <returns>The validation schema, or null if not found.</returns>
    public ValidationSchema? GetSchema(string canonicalUrl)
    {
        if (string.IsNullOrEmpty(canonicalUrl))
        {
            return null;
        }

        // GetOrAdd pattern: atomic cache lookup/build operation
        return _cache.GetOrAdd(canonicalUrl, url => _inner.GetSchema(url));
    }

    /// <summary>
    /// Clears all cached schemas.
    /// Useful for testing or when schema definitions change dynamically.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets the current number of cached schemas.
    /// </summary>
    public int CacheCount => _cache.Count;
}
