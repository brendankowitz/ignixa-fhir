// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Ignixa.Application.Features.Bundle;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Extension methods for HttpContext to support bundle processing.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Gets the DeferredWriteCoordinator from HttpContext.Items if present.
    /// Used during bundle processing to coordinate deferred batch writes.
    /// When a bundle is being processed, the coordinator is stored in HttpContext.Items
    /// to allow handlers to queue writes instead of executing them immediately.
    /// </summary>
    /// <param name="httpContext">The HttpContext to check.</param>
    /// <returns>The DeferredWriteCoordinator if found in bundle context, otherwise null.</returns>
    public static DeferredWriteCoordinator? GetDeferredWriteCoordinator(this HttpContext? httpContext)
    {
        if (httpContext?.Items.TryGetValue("DeferredWriteCoordinator", out var coordinatorObj) == true)
        {
            return coordinatorObj as DeferredWriteCoordinator;
        }

        return null;
    }

    /// <summary>
    /// Gets the bundle entry index from HttpContext.Items if present.
    /// Used during bundle processing to track which entry is being processed.
    /// This index is used for error reporting and maintaining the order of operations
    /// within a bundle transaction.
    /// </summary>
    /// <param name="httpContext">The HttpContext to check.</param>
    /// <returns>The bundle entry index if found, otherwise 0.</returns>
    public static int GetBundleEntryIndex(this HttpContext? httpContext)
    {
        if (httpContext?.Items.TryGetValue("BundleEntryIndex", out var indexObj) == true && indexObj is int index)
        {
            return index;
        }

        return 0;
    }
}
