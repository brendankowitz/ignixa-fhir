// <copyright file="HistoryCountHelper.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Helper methods for counting history results (for _total=accurate).
/// Uses the repository's body-free count operations, independently of page limits.
/// </summary>
public static class HistoryCountHelper
{
    /// <summary>
    /// Counts total number of versions for a resource instance.
    /// Includes tombstones and applies the time filters, ignoring pagination.
    /// </summary>
    public static Task<int> CountResourceHistoryAsync(
        IFhirRepository repository,
        ResourceKey key,
        HistoryQueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return repository.CountResourceHistoryAsync(key, parameters, cancellationToken);
    }

    /// <summary>
    /// Counts total number of versions for a resource type.
    /// Includes tombstones and applies the time filters, ignoring pagination.
    /// </summary>
    public static Task<int> CountTypeHistoryAsync(
        IFhirRepository repository,
        string resourceType,
        int tenantId,
        HistoryQueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return repository.CountTypeHistoryAsync(resourceType, tenantId, parameters, cancellationToken);
    }

    /// <summary>
    /// Counts total number of versions across all resource types.
    /// Includes tombstones and applies the time filters, ignoring pagination.
    /// </summary>
    public static Task<int> CountSystemHistoryAsync(
        IFhirRepository repository,
        int tenantId,
        HistoryQueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return repository.CountSystemHistoryAsync(tenantId, parameters, cancellationToken);
    }
}
