// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace Ignixa.Api.Extensions;

/// <summary>
/// Extension methods for HttpContext to simplify common operations.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Extracts the TenantId from HttpContext.Items.
    /// Throws InvalidOperationException if TenantId is not found or is not an int.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The tenant ID.</returns>
    /// <exception cref="InvalidOperationException">Thrown when TenantId is not found in HttpContext.Items or is not an int.</exception>
    public static int GetTenantId(this HttpContext context)
    {
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
        {
            throw new InvalidOperationException(
                "TenantId not found in HttpContext.Items. TenantResolutionMiddleware may not have run.");
        }

        return tenantId;
    }
}
