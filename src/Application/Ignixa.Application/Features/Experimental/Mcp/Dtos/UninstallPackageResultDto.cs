// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;

namespace Ignixa.Application.Features.Experimental.Mcp.Dtos;

/// <summary>
/// Result of uninstalling a package.
/// </summary>
public record UninstallPackageResultDto
{
    /// <summary>
    /// Whether the uninstallation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Tenant ID where package was uninstalled.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Tenant name.
    /// </summary>
    public required string TenantName { get; init; }

    /// <summary>
    /// Package ID that was uninstalled.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Package version that was uninstalled.
    /// </summary>
    public required string PackageVersion { get; init; }

    /// <summary>
    /// Number of resources deactivated.
    /// </summary>
    public required int DeactivatedResources { get; init; }

    /// <summary>
    /// Human-readable message summarizing the uninstallation.
    /// </summary>
    public required string Message { get; init; }
}
