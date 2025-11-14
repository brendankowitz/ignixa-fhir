// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

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

}
