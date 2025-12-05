// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Tracks all resources generated in a scenario for reference resolution.
/// Maintains a registry of resource identities by ID and optional logical name.
/// </summary>
internal sealed class ResourceRegistry
{
    private readonly Dictionary<string, ResourceIdentity> _byId = [];
    private readonly Dictionary<string, ResourceIdentity> _byLogicalName = [];

    /// <summary>
    /// Registers a resource with optional logical name.
    /// </summary>
    /// <param name="identity">The resource identity to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when identity is null.</exception>
    public void Register(ResourceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        _byId[identity.Id] = identity;

        if (!string.IsNullOrEmpty(identity.LogicalName))
        {
            _byLogicalName[identity.LogicalName] = identity;
        }
    }

    /// <summary>
    /// Gets resource identity by ID.
    /// </summary>
    /// <param name="id">The resource ID.</param>
    /// <returns>The resource identity, or null if not found.</returns>
    public ResourceIdentity? GetById(string id) =>
        _byId.TryGetValue(id, out var identity) ? identity : null;

    /// <summary>
    /// Gets resource identity by logical name.
    /// </summary>
    /// <param name="name">The logical name.</param>
    /// <returns>The resource identity, or null if not found.</returns>
    public ResourceIdentity? GetByLogicalName(string name) =>
        _byLogicalName.TryGetValue(name, out var identity) ? identity : null;

    /// <summary>
    /// Gets all registered identities indexed by resource ID.
    /// </summary>
    public IReadOnlyDictionary<string, ResourceIdentity> All => _byId;

    /// <summary>
    /// Gets the number of registered resources.
    /// </summary>
    public int Count => _byId.Count;

    /// <summary>
    /// Clears all registered resources.
    /// </summary>
    public void Clear()
    {
        _byId.Clear();
        _byLogicalName.Clear();
    }
}
