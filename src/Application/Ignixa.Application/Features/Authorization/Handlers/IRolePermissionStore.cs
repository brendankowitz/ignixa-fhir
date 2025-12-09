// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// Interface for role permission store.
/// Provides role-based permissions for RBAC authorization.
/// </summary>
public interface IRolePermissionStore
{
    /// <summary>
    /// Gets permissions for the specified roles in a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="roles">The roles to get permissions for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of permissions granted by the specified roles.</returns>
    ValueTask<IReadOnlyList<ResourceGrant>> GetPermissionsAsync(
        string tenantId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken);
}
