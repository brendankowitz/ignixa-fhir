// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Models;

/// <summary>
/// Result of an authorization check.
/// Contains the decision (allowed/denied), reason for denial, and any data filters to apply.
/// </summary>
public record AuthorizationResult
{
    /// <summary>
    /// Whether the request is authorized.
    /// </summary>
    public required bool Allowed { get; init; }

    /// <summary>
    /// Reason for denial. Only set when Allowed is false.
    /// Used in OperationOutcome diagnostics.
    /// </summary>
    public string? DenialReason { get; init; }

    /// <summary>
    /// Data filtering rules to apply to the request.
    /// Used for patient compartment filtering in SMART on FHIR.
    /// </summary>
    public FhirAuthorizationFilter? Filter { get; init; }

    /// <summary>
    /// Creates a successful authorization result with no filters.
    /// </summary>
    public static AuthorizationResult Success() => new() { Allowed = true };

    /// <summary>
    /// Creates a successful authorization result with data filters.
    /// </summary>
    /// <param name="filter">The data filter to apply.</param>
    public static AuthorizationResult SuccessWithFilter(FhirAuthorizationFilter filter) =>
        new() { Allowed = true, Filter = filter };

    /// <summary>
    /// Creates a denied authorization result.
    /// </summary>
    /// <param name="reason">The reason for denial.</param>
    public static AuthorizationResult Denied(string reason) =>
        new() { Allowed = false, DenialReason = reason };

    /// <summary>
    /// Creates a denied authorization result for authentication failure.
    /// </summary>
    public static AuthorizationResult AuthenticationRequired =>
        new() { Allowed = false, DenialReason = "Authentication required" };

    /// <summary>
    /// Creates a denied authorization result for tenant isolation violation.
    /// </summary>
    /// <param name="tenantId">The tenant ID that was accessed.</param>
    public static AuthorizationResult TenantAccessDenied(string tenantId) =>
        new() { Allowed = false, DenialReason = $"Access denied to tenant {tenantId}" };

    /// <summary>
    /// Creates a denied authorization result for insufficient permissions.
    /// </summary>
    /// <param name="resourceType">The resource type being accessed.</param>
    /// <param name="interaction">The interaction being attempted.</param>
    public static AuthorizationResult InsufficientPermissions(string resourceType, string interaction) =>
        new() { Allowed = false, DenialReason = $"No permission grants {interaction} access to {resourceType}" };

    /// <summary>
    /// Creates a denied authorization result for unsupported capability.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    /// <param name="interaction">The interaction.</param>
    public static AuthorizationResult CapabilityNotSupported(string? resourceType, string interaction) =>
        new() { Allowed = false, DenialReason = $"Server does not support {interaction} on {resourceType ?? "system"} (per CapabilityStatement)" };
}
