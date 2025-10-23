// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Ignixa.Application.Features.Bundle;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Extension methods for HttpContext and IHttpContextAccessor to support bundle processing and tenant context extraction.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Extracts the tenant ID from HttpContext.Items.
    /// Tenant ID is set by TenantResolutionMiddleware during request processing.
    /// </summary>
    /// <param name="accessor">The HTTP context accessor.</param>
    /// <returns>The tenant ID.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if HttpContext is null or TenantId is not found in HttpContext.Items.
    /// </exception>
    public static int GetTenantId(this IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        var httpContext = accessor.HttpContext
            ?? throw new InvalidOperationException(
                "HttpContext is null - tenant context required for this operation");

        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) ||
            tenantIdObj is not int tenantId)
        {
            throw new InvalidOperationException(
                "TenantId not found in HttpContext.Items. " +
                "TenantResolutionMiddleware may not have run.");
        }

        return tenantId;
    }

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
    /// AsyncLocal storage for bundle entry index (thread-safe for concurrent bundle processing).
    /// Using AsyncLocal instead of HttpContext.Items because HttpContext is shared across
    /// concurrent bundle entry executions, causing race conditions where entries overwrite
    /// each other's index values.
    /// </summary>
    private static readonly AsyncLocal<int> _bundleEntryIndex = new AsyncLocal<int>();

    /// <summary>
    /// Sets the bundle entry index for the current async execution context.
    /// Used by BundleEntryExecutor to pass entry index to handlers in a thread-safe manner.
    /// </summary>
    /// <param name="httpContext">The HttpContext (unused, kept for API compatibility).</param>
    /// <param name="entryIndex">The entry index to set.</param>
    public static void SetBundleEntryIndex(this HttpContext? httpContext, int entryIndex)
    {
        _bundleEntryIndex.Value = entryIndex;
    }

    /// <summary>
    /// Gets the bundle entry index from AsyncLocal storage.
    /// Used during bundle processing to track which entry is being processed.
    /// This index is used for error reporting and surrogate ID calculation.
    /// AsyncLocal ensures each concurrent bundle entry maintains its own index value.
    /// </summary>
    /// <param name="httpContext">The HttpContext (unused, kept for API compatibility).</param>
    /// <returns>The bundle entry index if found, otherwise 0.</returns>
    public static int GetBundleEntryIndex(this HttpContext? httpContext)
    {
        return _bundleEntryIndex.Value;
    }
}
