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
/// Counts by enumerating the full result set WITHOUT loading resource bytes.
/// Still expensive, but avoids loading full resources into memory.
/// </summary>
public static class HistoryCountHelper
{
    /// <summary>
    /// Counts total number of versions for a resource instance.
    /// Enumerates full result set without offset/limit to get accurate count.
    /// </summary>
    public static async Task<int> CountResourceHistoryAsync(
        IFhirRepository repository,
        ResourceKey key,
        HistoryQueryParameters parameters,
        CancellationToken ct = default)
    {
        // Create parameters with no pagination (offset=0, count=MaxValue)
        var countParameters = parameters with
        {
            Offset = 0,
            Count = int.MaxValue
        };

        int count = 0;
        await foreach (var entry in repository.GetResourceHistoryAsync(key, countParameters, ct))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Counts total number of versions for a resource type.
    /// Enumerates full result set without offset/limit to get accurate count.
    /// </summary>
    public static async Task<int> CountTypeHistoryAsync(
        IFhirRepository repository,
        string resourceType,
        int tenantId,
        HistoryQueryParameters parameters,
        CancellationToken ct = default)
    {
        // Create parameters with no pagination (offset=0, count=MaxValue)
        var countParameters = parameters with
        {
            Offset = 0,
            Count = int.MaxValue
        };

        int count = 0;
        await foreach (var entry in repository.GetTypeHistoryAsync(resourceType, tenantId, countParameters, ct))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Counts total number of versions across all resource types.
    /// Enumerates full result set without offset/limit to get accurate count.
    /// </summary>
    public static async Task<int> CountSystemHistoryAsync(
        IFhirRepository repository,
        int tenantId,
        HistoryQueryParameters parameters,
        CancellationToken ct = default)
    {
        // Create parameters with no pagination (offset=0, count=MaxValue)
        var countParameters = parameters with
        {
            Offset = 0,
            Count = int.MaxValue
        };

        int count = 0;
        await foreach (var entry in repository.GetSystemHistoryAsync(tenantId, countParameters, ct))
        {
            count++;
        }

        return count;
    }
}
