// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Smart;

/// <summary>
/// SMART on FHIR v2 scope type indicating the context of the access request.
/// Per SMART App Launch v2.2.0 specification.
/// </summary>
public enum SmartScopeType
{
    /// <summary>
    /// Patient-level scope (patient/*.rs).
    /// Access is restricted to resources within the patient's compartment.
    /// </summary>
    Patient,

    /// <summary>
    /// User-level scope (user/*.rs).
    /// Access is based on the user's role/permissions, not restricted to a specific patient.
    /// </summary>
    User,

    /// <summary>
    /// System-level scope (system/*.rs).
    /// Full access for backend services (client credentials flow).
    /// </summary>
    System,

    /// <summary>
    /// Practitioner-level scope (practitioner/*.rs).
    /// Access is restricted to resources within the practitioner's compartment.
    /// Supported in SMART v2 for practitioner-facing applications.
    /// </summary>
    Practitioner
}

/// <summary>
/// SMART v2 CRUDS permissions.
/// Permissions MUST appear in the order: c, r, u, d, s.
/// </summary>
[Flags]
public enum SmartPermissions
{
    /// <summary>No permissions.</summary>
    None = 0,

    /// <summary>Create permission (c).</summary>
    Create = 1,

    /// <summary>Read permission (r).</summary>
    Read = 2,

    /// <summary>Update permission (u).</summary>
    Update = 4,

    /// <summary>Delete permission (d).</summary>
    Delete = 8,

    /// <summary>Search permission (s).</summary>
    Search = 16,

    /// <summary>All permissions (cruds).</summary>
    All = Create | Read | Update | Delete | Search
}

/// <summary>
/// Represents a parsed SMART on FHIR v2 scope.
/// Scopes follow the pattern: [context]/[resource].[permissions][?search-constraints]
/// Examples:
///   patient/Observation.rs (read + search)
///   user/Medication.cruds (all permissions)
///   system/Patient.cud (create, update, delete)
///   patient/Observation.rs?category=http://terminology.hl7.org/CodeSystem/observation-category|laboratory
/// </summary>
public record SmartScope
{
    /// <summary>
    /// The scope type (patient, user, system, or practitioner).
    /// </summary>
    public required SmartScopeType Type { get; init; }

    /// <summary>
    /// The resource type ("*" for all resources, or specific type like "Patient").
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// The SMART v2 CRUDS permissions as flags.
    /// </summary>
    public required SmartPermissions Permissions { get; init; }

    /// <summary>
    /// The permission string in canonical CRUDS order (e.g., "cruds", "rs", "cud").
    /// </summary>
    public required string PermissionString { get; init; }

    /// <summary>
    /// Optional search parameter constraints (e.g., "category=laboratory").
    /// SMART v2 allows constraining access using FHIR search parameters.
    /// </summary>
    public IReadOnlyDictionary<string, string>? SearchConstraints { get; init; }

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
    /// Checks if this scope grants a specific permission.
    /// </summary>
    /// <param name="permission">The permission to check (create, read, update, delete, search).</param>
    /// <returns>True if the scope grants this permission.</returns>
    public bool HasPermission(SmartPermissions permission)
    {
        return (Permissions & permission) == permission;
    }

    /// <summary>
    /// Checks if this scope grants a specific interaction permission.
    /// Maps FHIR interactions to SMART v2 CRUDS permissions.
    /// </summary>
    /// <param name="interaction">The FHIR interaction (read, create, update, delete, search-type, etc.).</param>
    /// <returns>True if the scope grants this permission.</returns>
    public bool MatchesInteraction(string interaction)
    {
        var required = MapInteractionToPermission(interaction);
        return HasPermission(required);
    }

    /// <summary>
    /// Checks if this scope matches both a resource type and interaction permission.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    /// <param name="interaction">The FHIR interaction.</param>
    /// <returns>True if scope grants access.</returns>
    public bool Matches(string? resourceType, string interaction)
    {
        return MatchesResource(resourceType) && MatchesInteraction(interaction);
    }

    /// <summary>
    /// Maps a FHIR interaction to SMART v2 permission.
    /// </summary>
    private static SmartPermissions MapInteractionToPermission(string interaction)
    {
        return interaction.ToUpperInvariant() switch
        {
            "CREATE" => SmartPermissions.Create,
            "READ" => SmartPermissions.Read,
            "VREAD" => SmartPermissions.Read,
            "UPDATE" => SmartPermissions.Update,
            "PATCH" => SmartPermissions.Update,
            "DELETE" => SmartPermissions.Delete,
            "SEARCH" => SmartPermissions.Search,
            "SEARCH-TYPE" => SmartPermissions.Search,
            "SEARCH-SYSTEM" => SmartPermissions.Search,
            "HISTORY" => SmartPermissions.Read,
            "HISTORY-INSTANCE" => SmartPermissions.Read,
            "HISTORY-TYPE" => SmartPermissions.Read,
            _ => SmartPermissions.None
        };
    }
}
