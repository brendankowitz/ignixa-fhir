// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Smart;

/// <summary>
/// SMART on FHIR scope type indicating the context of the access request.
/// </summary>
public enum SmartScopeType
{
    /// <summary>
    /// Patient-level scope (patient/*.read).
    /// Access is restricted to resources within the patient's compartment.
    /// </summary>
    Patient,

    /// <summary>
    /// User-level scope (user/*.read).
    /// Access is based on the user's role/permissions, not restricted to a specific patient.
    /// </summary>
    User,

    /// <summary>
    /// System-level scope (system/*.read).
    /// Full access for backend services (client credentials flow).
    /// </summary>
    System
}

/// <summary>
/// Represents a parsed SMART on FHIR scope.
/// Scopes follow the pattern: [context]/[resource].[permission]
/// Examples: patient/Observation.read, user/*.write, system/Patient.*
/// </summary>
public record SmartScope
{
    /// <summary>
    /// The scope type (patient, user, or system).
    /// </summary>
    public required SmartScopeType Type { get; init; }

    /// <summary>
    /// The resource type ("*" for all resources, or specific type like "Patient").
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// The permission type ("read", "write", "create", "update", "delete", or "*" for all).
    /// SMART v2 also supports "c" (create), "r" (read), "u" (update), "d" (delete), "s" (search).
    /// </summary>
    public required string Permission { get; init; }

    /// <summary>
    /// The original scope string.
    /// </summary>
    public required string OriginalScope { get; init; }

    /// <summary>
    /// Checks if this scope matches a resource type.
    /// </summary>
    /// <param name="resourceType">The resource type to check.</param>
    /// <returns>True if the scope covers this resource type.</returns>
    public bool MatchesResource(string? resourceType)
    {
        if (resourceType == null)
        {
            // System-level operations match if scope has * resource type
            return ResourceType == "*";
        }

        return ResourceType == "*" ||
               string.Equals(ResourceType, resourceType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if this scope grants a specific permission type.
    /// Maps SMART permissions to the FHIR interaction permission.
    /// </summary>
    /// <param name="permission">The permission type to check (read, create, update, delete).</param>
    /// <returns>True if the scope grants this permission.</returns>
    public bool MatchesPermission(string permission)
    {
        // Wildcard matches everything
        if (Permission == "*")
        {
            return true;
        }

        // Exact match
        if (string.Equals(Permission, permission, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // "write" grants create, update, and delete
        if (string.Equals(Permission, "write", StringComparison.OrdinalIgnoreCase))
        {
            return permission is "create" or "update" or "delete";
        }

        // SMART v2 granular permissions
        return Permission.ToUpperInvariant() switch
        {
            "C" => string.Equals(permission, "create", StringComparison.OrdinalIgnoreCase),
            "R" => string.Equals(permission, "read", StringComparison.OrdinalIgnoreCase),
            "U" => string.Equals(permission, "update", StringComparison.OrdinalIgnoreCase),
            "D" => string.Equals(permission, "delete", StringComparison.OrdinalIgnoreCase),
            "S" => string.Equals(permission, "read", StringComparison.OrdinalIgnoreCase), // search = read
            "CRU" or "CRUD" or "CRUDS" => true, // Combined permissions
            _ => false
        };
    }

    /// <summary>
    /// Checks if this scope matches both a resource type and interaction permission.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    /// <param name="interaction">The interaction permission (read, create, update, delete).</param>
    /// <returns>True if scope grants access.</returns>
    public bool Matches(string? resourceType, string interaction)
    {
        return MatchesResource(resourceType) && MatchesPermission(interaction);
    }
}
