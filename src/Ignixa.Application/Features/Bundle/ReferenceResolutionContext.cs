// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Context for resolving urn:uuid references within a bundle.
/// Maps temporary urn:uuid identifiers to actual server-assigned resource IDs.
/// Thread-safe for concurrent access during parallel bundle processing.
/// </summary>
public class ReferenceResolutionContext
{
    private readonly ConcurrentDictionary<string, string> _referenceMap = new();

    /// <summary>
    /// Gets the reference map (urn:uuid -> actual resource ID).
    /// </summary>
    public IReadOnlyDictionary<string, string> ReferenceMap => _referenceMap;

    /// <summary>
    /// Adds a reference mapping from urn:uuid to an actual resource ID.
    /// </summary>
    /// <param name="urnUuid">The urn:uuid identifier (e.g., "urn:uuid:05efabf0-4be2-4561-91ce-51548425acb9").</param>
    /// <param name="actualId">The actual server-assigned resource ID (e.g., "a1b2c3d4-e5f6-7890-abcd-ef1234567890").</param>
    /// <exception cref="ArgumentNullException">If urnUuid or actualId is null or whitespace.</exception>
    public void AddReference(string urnUuid, string actualId)
    {
        if (string.IsNullOrWhiteSpace(urnUuid))
        {
            throw new ArgumentNullException(nameof(urnUuid), "urn:uuid cannot be null or whitespace");
        }

        if (string.IsNullOrWhiteSpace(actualId))
        {
            throw new ArgumentNullException(nameof(actualId), "Actual ID cannot be null or whitespace");
        }

        // Ensure urn:uuid format
        if (!urnUuid.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid urn:uuid format: {urnUuid}", nameof(urnUuid));
        }

        _referenceMap[urnUuid] = actualId;
    }

    /// <summary>
    /// Attempts to resolve a urn:uuid reference to an actual resource ID.
    /// </summary>
    /// <param name="urnUuid">The urn:uuid identifier to resolve.</param>
    /// <returns>The actual resource ID if found; otherwise, null.</returns>
    public string? ResolveReference(string urnUuid)
    {
        if (string.IsNullOrWhiteSpace(urnUuid))
        {
            return null;
        }

        return _referenceMap.TryGetValue(urnUuid, out var actualId) ? actualId : null;
    }

    /// <summary>
    /// Checks if a reference mapping exists for the given urn:uuid.
    /// </summary>
    /// <param name="urnUuid">The urn:uuid identifier to check.</param>
    /// <returns>True if a mapping exists; otherwise, false.</returns>
    public bool HasReference(string urnUuid)
    {
        return !string.IsNullOrWhiteSpace(urnUuid) && _referenceMap.ContainsKey(urnUuid);
    }

    /// <summary>
    /// Gets the total number of reference mappings.
    /// </summary>
    public int Count => _referenceMap.Count;

    /// <summary>
    /// Clears all reference mappings.
    /// </summary>
    public void Clear()
    {
        _referenceMap.Clear();
    }
}
