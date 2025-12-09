// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Application.Features.Authorization.Models;

/// <summary>
/// Represents a FHIR resource access grant for RBAC authorization.
/// Combines resource type and interaction to define what operations are allowed.
/// </summary>
/// <param name="ResourceType">The resource type (e.g., "Patient", "Observation") or "*" for all types.</param>
/// <param name="Interaction">The interaction type (e.g., "read", "create") or "*" for all interactions.</param>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "ResourceGrant accurately describes this record's purpose")]
public record ResourceGrant(string ResourceType, string Interaction)
{
    /// <summary>
    /// Permission that grants access to all resources and all interactions.
    /// </summary>
    public static readonly ResourceGrant All = new("*", "*");

    /// <summary>
    /// Checks if this permission matches a required permission.
    /// Supports wildcards: "*" matches any resource type or interaction.
    /// </summary>
    /// <param name="required">The required permission to check against.</param>
    /// <returns>True if this permission grants the required access, false otherwise.</returns>
    public bool Matches(ResourceGrant required)
    {
        var resourceMatch = ResourceType == "*" ||
                           string.Equals(ResourceType, required.ResourceType, StringComparison.OrdinalIgnoreCase);

        var interactionMatch = Interaction == "*" ||
                              string.Equals(Interaction, required.Interaction, StringComparison.OrdinalIgnoreCase);

        return resourceMatch && interactionMatch;
    }

    /// <summary>
    /// Creates a permission for read-only access to a specific resource type.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    /// <returns>A read-only permission for the specified resource type.</returns>
    public static ResourceGrant ReadOnly(string resourceType) => new(resourceType, "read");

    /// <summary>
    /// Creates a permission for all interactions on a specific resource type.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    /// <returns>A full access permission for the specified resource type.</returns>
    public static ResourceGrant FullAccess(string resourceType) => new(resourceType, "*");

    /// <summary>
    /// Creates a permission for read-only access to all resource types.
    /// </summary>
    /// <returns>A global read-only permission.</returns>
    public static ResourceGrant GlobalReadOnly() => new("*", "read");
}
