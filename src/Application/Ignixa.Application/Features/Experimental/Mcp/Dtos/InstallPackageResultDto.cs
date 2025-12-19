// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;

namespace Ignixa.Application.Features.Experimental.Mcp.Dtos;

/// <summary>
/// Result of installing a package.
/// </summary>
public record InstallPackageResultDto
{
    /// <summary>
    /// Whether the installation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Tenant ID where package was installed.
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// Tenant name.
    /// </summary>
    public required string TenantName { get; init; }

    /// <summary>
    /// Package ID that was installed.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Package version that was installed.
    /// </summary>
    public required string PackageVersion { get; init; }

    /// <summary>
    /// Total number of resources extracted from package.
    /// </summary>
    public required int TotalResources { get; init; }

    /// <summary>
    /// Number of new resources imported.
    /// </summary>
    public required int ImportedResources { get; init; }

    /// <summary>
    /// Number of existing resources updated.
    /// </summary>
    public required int UpdatedResources { get; init; }

    /// <summary>
    /// Duration of installation in seconds.
    /// </summary>
    public required int DurationSeconds { get; init; }

    /// <summary>
    /// Breakdown of resources by type.
    /// </summary>
    public required Dictionary<string, int> ResourcesByType { get; init; }

    /// <summary>
    /// Human-readable message summarizing the installation.
    /// </summary>
    public required string Message { get; init; }
}
