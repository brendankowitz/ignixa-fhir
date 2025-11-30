// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.DataLayer.InMemoryIndex;

/// <summary>
/// Tracks which data layer(s) contain each resource.
/// Used in Distributed mode to route queries to the correct layer(s).
/// </summary>
public interface IResourceLocationIndex
{
    /// <summary>
    /// Records that a resource exists in a specific data layer.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="dataLayerName">The name of the data layer containing this resource.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask AddAsync(ResourceKey key, string dataLayerName, CancellationToken ct = default);

    /// <summary>
    /// Gets all data layers that contain the specified resource.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of data layer names, or empty if resource not found.</returns>
    ValueTask<IReadOnlyCollection<string>> GetLayersAsync(ResourceKey key, CancellationToken ct = default);

    /// <summary>
    /// Removes a resource from the index for a specific data layer.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="dataLayerName">The name of the data layer.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveAsync(ResourceKey key, string dataLayerName, CancellationToken ct = default);

    /// <summary>
    /// Gets all data layer names that are registered in the index.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Collection of all data layer names.</returns>
    ValueTask<IReadOnlyCollection<string>> GetAllLayerNamesAsync(CancellationToken ct = default);
}
