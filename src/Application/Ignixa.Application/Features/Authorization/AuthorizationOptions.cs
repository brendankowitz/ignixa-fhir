// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;

namespace Ignixa.Application.Features.Authorization;

/// <summary>
/// Configuration options for FHIR authorization.
/// </summary>
public class AuthorizationOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Authorization";

    /// <summary>
    /// Whether authorization is enabled. Default: true.
    /// Set to false to bypass all authorization checks (development only).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to require authentication for all endpoints (except /metadata).
    /// Default: true.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// Whether to enforce tenant isolation. Default: true.
    /// </summary>
    public bool EnforceTenantIsolation { get; set; } = true;

    /// <summary>
    /// Whether to enforce CapabilityStatement compliance. Default: true.
    /// When enabled, requests for unsupported interactions are rejected.
    /// </summary>
    public bool EnforceCapabilities { get; set; } = true;

    /// <summary>
    /// Default role permissions.
    /// Maps role names to arrays of permissions.
    /// </summary>
    public Dictionary<string, RolePermissions> DefaultRoles { get; } = new();

    /// <summary>
    /// Roles that have MCP (Model Context Protocol) access enabled by default.
    /// These roles can access MCP endpoints for AI/automation, in addition to
    /// any role with McpAccess=true in DefaultRoles configuration.
    /// </summary>
    public Collection<string> McpEnabledRoles { get; } = ["Admin", "SystemAdmin", "Mcp", "Contributor"];

    /// <summary>
    /// SMART on FHIR configuration options.
    /// </summary>
    public SmartOptions SmartOnFhir { get; set; } = new();
}

/// <summary>
/// Permissions configuration for a role.
/// </summary>
public class RolePermissions
{
    /// <summary>
    /// List of permissions granted to this role.
    /// </summary>
    public Collection<PermissionEntry> Permissions { get; } = new();

    /// <summary>
    /// Whether this role grants access to MCP (Model Context Protocol) tools.
    /// When true, users with this role can access MCP endpoints for AI/automation.
    /// </summary>
    public bool McpAccess { get; set; }
}

/// <summary>
/// A single permission entry.
/// </summary>
public class PermissionEntry
{
    /// <summary>
    /// Resource type ("*" for all).
    /// </summary>
    public string ResourceType { get; set; } = "*";

    /// <summary>
    /// Interaction type ("*" for all).
    /// </summary>
    public string Interaction { get; set; } = "*";
}
