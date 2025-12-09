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
    /// Handler configurations.
    /// </summary>
    public Collection<HandlerConfiguration> Handlers { get; } = new();

    /// <summary>
    /// Default role permissions.
    /// Maps role names to arrays of permissions.
    /// </summary>
    public Dictionary<string, RolePermissions> DefaultRoles { get; } = new();
}

/// <summary>
/// Configuration for an individual authorization handler.
/// </summary>
public class HandlerConfiguration
{
    /// <summary>
    /// Handler type name.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Handler priority (lower = earlier execution).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether this handler is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
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
