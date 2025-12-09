// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Ignixa.Application.Features.Authorization.Services;

/// <summary>
/// Cache for O(1) lookup of supported FHIR interactions.
/// Built from CapabilityStatement at startup or on cache refresh.
/// Key: "{resourceType}:{interaction}" or "_system:{interaction}" for system-level.
/// </summary>
public class CapabilityInteractionCache
{
    private readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a supported interaction to the cache.
    /// </summary>
    /// <param name="resourceType">The resource type (or "_system" for system-level).</param>
    /// <param name="interactionCode">The FHIR interaction code.</param>
    public void AddInteraction(string resourceType, string interactionCode)
    {
        _cache[$"{resourceType}:{interactionCode}"] = true;
    }

    /// <summary>
    /// Checks if an interaction is supported.
    /// </summary>
    /// <param name="resourceType">The resource type (null for system-level).</param>
    /// <param name="interactionCode">The FHIR interaction code.</param>
    /// <returns>True if the interaction is supported.</returns>
    public bool IsSupported(string? resourceType, string interactionCode)
    {
        var key = resourceType == null
            ? $"_system:{interactionCode}"
            : $"{resourceType}:{interactionCode}";

        return _cache.TryGetValue(key, out var supported) && supported;
    }

    /// <summary>
    /// Clears all cached interactions.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets all cached interaction keys (for diagnostics).
    /// </summary>
    public IEnumerable<string> CachedKeys => _cache.Keys;

    /// <summary>
    /// Gets the count of cached interactions.
    /// </summary>
    public int Count => _cache.Count;
}
