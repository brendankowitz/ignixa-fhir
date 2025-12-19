// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Experimental.Mcp.Authorization;

/// <summary>
/// Implementation of MCP authorization service.
/// Validates that users have appropriate roles for MCP operations.
/// </summary>
public class McpAuthorizationService : IMcpAuthorizationService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRolePermissionStore _rolePermissionStore;
    private readonly AuthorizationOptions _authzOptions;
    private readonly ILogger<McpAuthorizationService> _logger;
    private readonly HashSet<string> _mcpEnabledRoles;

    public McpAuthorizationService(
        IHttpContextAccessor httpContextAccessor,
        IRolePermissionStore rolePermissionStore,
        IOptions<AuthorizationOptions> authzOptions,
        ILogger<McpAuthorizationService> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _rolePermissionStore = rolePermissionStore ?? throw new ArgumentNullException(nameof(rolePermissionStore));
        _authzOptions = authzOptions?.Value ?? throw new ArgumentNullException(nameof(authzOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Build the set of MCP-enabled roles from configuration
        _mcpEnabledRoles = new HashSet<string>(_authzOptions.McpEnabledRoles, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<bool> AuthorizeMcpAccessAsync(CancellationToken cancellationToken = default)
    {
        var roles = GetCurrentUserRoles();

        if (roles.Count == 0)
        {
            _logger.LogWarning("MCP access denied: No roles found for current user");
            return Task.FromResult(false);
        }

        // Check if any role has MCP access
        var hasAccess = roles.Any(HasMcpAccess);

        if (!hasAccess)
        {
            _logger.LogWarning(
                "MCP access denied: User roles [{Roles}] do not have MCP access. Configured MCP roles: [{McpRoles}]",
                string.Join(", ", roles),
                string.Join(", ", _mcpEnabledRoles));
        }

        return Task.FromResult(hasAccess);
    }

    /// <inheritdoc />
    public async Task<bool> AuthorizeOperationAsync(
        McpOperationType operationType,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        // First check MCP access
        if (!await AuthorizeMcpAccessAsync(cancellationToken))
        {
            return false;
        }

        var roles = GetCurrentUserRoles();
        var interaction = MapOperationToInteraction(operationType);

        // Get permissions for user's roles
        var permissions = await _rolePermissionStore.GetPermissionsAsync(
            tenantId: string.Empty, // MCP operations are tenant-agnostic at auth level
            roles,
            cancellationToken);

        // Check if any permission matches
        var requiredPermission = new ResourceGrant(resourceType ?? "*", interaction);
        var hasPermission = permissions.Any(p => p.Matches(requiredPermission));

        if (!hasPermission)
        {
            _logger.LogWarning(
                "MCP operation denied: User roles [{Roles}] do not have '{Interaction}' permission for resource type '{ResourceType}'",
                string.Join(", ", roles),
                interaction,
                resourceType ?? "*");
        }

        return hasPermission;
    }

    /// <inheritdoc />
    public async Task EnsureMcpAccessAsync(CancellationToken cancellationToken = default)
    {
        if (!await AuthorizeMcpAccessAsync(cancellationToken))
        {
            var configuredRoles = string.Join(", ", _mcpEnabledRoles);
            throw new ForbiddenException(
                $"MCP access denied. Required role: {configuredRoles} or a role with McpAccess enabled.");
        }
    }

    /// <inheritdoc />
    public async Task EnsureOperationAuthorizedAsync(
        McpOperationType operationType,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        if (!await AuthorizeOperationAsync(operationType, resourceType, cancellationToken))
        {
            var interaction = MapOperationToInteraction(operationType);
            throw new ForbiddenException(
                $"MCP operation denied. User does not have '{interaction}' permission for resource type '{resourceType ?? "*"}'.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetCurrentUserRoles()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User == null)
        {
            return Array.Empty<string>();
        }

        var roles = new List<string>();

        // Check multiple claim types for roles
        foreach (var claimType in new[] { FhirClaimTypes.Role, FhirClaimTypes.Roles, FhirClaimTypes.WsFederationRole })
        {
            var roleClaims = httpContext.User.FindAll(claimType);
            foreach (var claim in roleClaims)
            {
                if (!string.IsNullOrWhiteSpace(claim.Value))
                {
                    // Handle comma-separated roles (some IdPs do this)
                    var roleValues = claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    roles.AddRange(roleValues.Select(r => r.Trim()));
                }
            }
        }

        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool HasMcpAccess(string role)
    {
        // Check if role is in configured MCP-enabled roles
        if (_mcpEnabledRoles.Contains(role))
        {
            return true;
        }

        // Check DefaultRoles configuration for per-role McpAccess setting
        if (_authzOptions.DefaultRoles.TryGetValue(role, out var roleConfig))
        {
            return roleConfig.McpAccess;
        }

        return false;
    }

    private static string MapOperationToInteraction(McpOperationType operationType)
    {
        return operationType switch
        {
            McpOperationType.Read => "read",
            McpOperationType.Create => "create",
            McpOperationType.Update => "update",
            McpOperationType.Delete => "delete",
            McpOperationType.Admin => "*", // Admin operations require wildcard permission
            _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, null)
        };
    }
}
