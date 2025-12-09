// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// Authorization handler that checks role-based permissions (RBAC).
/// Skipped when SMART scopes are present (SMART takes precedence).
/// Priority: 30 (runs after tenant isolation).
/// </summary>
public class RbacAuthorizationHandler : IAuthorizationHandler
{
    private readonly IRolePermissionStore _permissionStore;
    private readonly ILogger<RbacAuthorizationHandler> _logger;

    public RbacAuthorizationHandler(
        IRolePermissionStore permissionStore,
        ILogger<RbacAuthorizationHandler> logger)
    {
        _permissionStore = permissionStore ?? throw new ArgumentNullException(nameof(permissionStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Priority => 30;

    /// <inheritdoc />
    public async ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        // Skip if using SMART scopes (SMART authorization takes precedence)
        if (context.HasSmartScopes)
        {
            _logger.LogDebug("RBAC check: Skipping - SMART scopes present");
            return AuthorizationResult.Success();
        }

        // Skip if no roles (let next handler decide - might be capability-only check)
        if (!context.HasRoles)
        {
            _logger.LogDebug("RBAC check: No roles present - passing to next handler");
            return AuthorizationResult.Success();
        }

        // Get permissions for user's roles
        var permissions = await _permissionStore.GetPermissionsAsync(
            context.TenantId ?? "default",
            context.Roles!,
            cancellationToken);

        // Build required permission from request
        var requiredPermission = context.RequiredPermission;

        _logger.LogDebug(
            "RBAC check: User {UserId} with roles [{Roles}] requesting {ResourceType}.{Interaction}",
            context.UserId,
            string.Join(", ", context.Roles!),
            requiredPermission.ResourceType,
            requiredPermission.Interaction);

        // Check if any permission matches the required permission
        if (!permissions.Any(p => p.Matches(requiredPermission)))
        {
            _logger.LogWarning(
                "RBAC check: Request denied - no permission grants {Interaction} access to {ResourceType} for roles [{Roles}]",
                requiredPermission.Interaction,
                requiredPermission.ResourceType,
                string.Join(", ", context.Roles!));

            return AuthorizationResult.InsufficientPermissions(
                requiredPermission.ResourceType,
                requiredPermission.Interaction);
        }

        _logger.LogDebug(
            "RBAC check: User {UserId} authorized for {ResourceType}.{Interaction}",
            context.UserId,
            requiredPermission.ResourceType,
            requiredPermission.Interaction);

        return AuthorizationResult.Success();
    }
}
