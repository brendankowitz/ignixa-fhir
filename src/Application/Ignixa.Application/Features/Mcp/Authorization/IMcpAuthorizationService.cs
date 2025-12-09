// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Mcp.Authorization;

/// <summary>
/// Defines MCP operation types for authorization.
/// Maps to FHIR interactions for permission checks.
/// </summary>
public enum McpOperationType
{
    /// <summary>
    /// Read operations (export, get, search, list, status checks).
    /// Maps to FHIR "read" interaction.
    /// </summary>
    Read,

    /// <summary>
    /// Create operations (import with IncrementalLoad mode, create resource).
    /// Maps to FHIR "create" interaction.
    /// </summary>
    Create,

    /// <summary>
    /// Update operations (patch, update resource).
    /// Maps to FHIR "update" interaction.
    /// </summary>
    Update,

    /// <summary>
    /// Delete operations (delete resource, uninstall).
    /// Maps to FHIR "delete" interaction.
    /// </summary>
    Delete,

    /// <summary>
    /// Administrative operations (tenant management, package management).
    /// Requires Admin or SystemAdmin role.
    /// </summary>
    Admin
}

/// <summary>
/// Service for authorizing MCP operations.
/// Ensures users have the appropriate role-based permissions before executing MCP tools.
/// </summary>
public interface IMcpAuthorizationService
{
    /// <summary>
    /// Authorizes the current user for MCP access.
    /// Checks if the user has an MCP-enabled role (Admin, SystemAdmin, Mcp, Contributor).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if authorized, false otherwise.</returns>
    Task<bool> AuthorizeMcpAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes an MCP operation for the current user.
    /// Checks both MCP access and the specific operation type permission.
    /// </summary>
    /// <param name="operationType">The type of MCP operation being performed.</param>
    /// <param name="resourceType">The FHIR resource type (optional, for resource-specific checks).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if authorized, false otherwise.</returns>
    Task<bool> AuthorizeOperationAsync(
        McpOperationType operationType,
        string? resourceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates MCP access and throws ForbiddenException if not authorized.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Domain.Exceptions.ForbiddenException">
    /// Thrown when the user does not have MCP access.
    /// </exception>
    Task EnsureMcpAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an MCP operation and throws ForbiddenException if not authorized.
    /// </summary>
    /// <param name="operationType">The type of MCP operation being performed.</param>
    /// <param name="resourceType">The FHIR resource type (optional, for resource-specific checks).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Domain.Exceptions.ForbiddenException">
    /// Thrown when the user does not have permission for the operation.
    /// </exception>
    Task EnsureOperationAuthorizedAsync(
        McpOperationType operationType,
        string? resourceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the roles of the current user.
    /// </summary>
    /// <returns>List of role names.</returns>
    IReadOnlyList<string> GetCurrentUserRoles();
}
