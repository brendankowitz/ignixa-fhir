// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// In-memory implementation of IRolePermissionStore.
/// Loads permissions from configuration (appsettings.json).
/// Production implementations may use database or external identity provider.
/// </summary>
public class InMemoryRolePermissionStore : IRolePermissionStore
{
    private readonly Dictionary<string, ResourceGrant[]> _rolePermissions;

    public InMemoryRolePermissionStore(IOptions<AuthorizationOptions> options)
    {
        var authOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _rolePermissions = new Dictionary<string, ResourceGrant[]>(StringComparer.OrdinalIgnoreCase);

        // Load permissions from configuration
        foreach (var (roleName, rolePerms) in authOptions.DefaultRoles)
        {
            _rolePermissions[roleName] = rolePerms.Permissions
                .Select(p => new ResourceGrant(p.ResourceType, p.Interaction))
                .ToArray();
        }

        // Add default roles if not configured
        if (!_rolePermissions.ContainsKey("Admin"))
        {
            _rolePermissions["Admin"] = new[] { ResourceGrant.All };
        }

        if (!_rolePermissions.ContainsKey("SystemAdmin"))
        {
            _rolePermissions["SystemAdmin"] = new[] { ResourceGrant.All };
        }

        if (!_rolePermissions.ContainsKey("Clinician"))
        {
            _rolePermissions["Clinician"] = new[]
            {
                new ResourceGrant("Patient", "*"),
                new ResourceGrant("Observation", "*"),
                new ResourceGrant("Encounter", "*"),
                new ResourceGrant("Condition", "*"),
                new ResourceGrant("Procedure", "*"),
                new ResourceGrant("MedicationRequest", "*"),
                new ResourceGrant("DiagnosticReport", "*"),
                new ResourceGrant("Practitioner", "read"),
                new ResourceGrant("Organization", "read")
            };
        }

        if (!_rolePermissions.ContainsKey("ReadOnly"))
        {
            _rolePermissions["ReadOnly"] = new[] { ResourceGrant.GlobalReadOnly() };
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ResourceGrant>> GetPermissionsAsync(
        string tenantId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        // Note: tenantId is intentionally unused in this in-memory implementation.
        // Permissions are loaded from global configuration (appsettings.json) and apply to all tenants.
        // Per-tenant role customization is deferred to a future phase.
        // Production deployments can implement IRolePermissionStore with database
        // or external IdP integration for per-tenant role definitions.

        // Aggregate permissions from all roles
        var permissions = roles
            .Where(r => _rolePermissions.ContainsKey(r))
            .SelectMany(r => _rolePermissions[r])
            .Distinct()
            .ToList();

        return ValueTask.FromResult<IReadOnlyList<ResourceGrant>>(permissions);
    }

    /// <summary>
    /// Adds or updates permissions for a role (for testing/runtime configuration).
    /// </summary>
    public void SetRolePermissions(string role, ResourceGrant[] permissions)
    {
        _rolePermissions[role] = permissions;
    }

    /// <summary>
    /// Gets all configured role names.
    /// </summary>
    public IEnumerable<string> RoleNames => _rolePermissions.Keys;
}
