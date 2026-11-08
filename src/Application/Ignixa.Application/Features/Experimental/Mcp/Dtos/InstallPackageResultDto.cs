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
    /// Whether the package was activated. False when it was stored but activation was rejected.
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

    /// <summary>
    /// Activation issues as "severity CODE: message". Errors mean the package was stored but not activated;
    /// warnings describe a durable activation whose definitions are not searchable yet or whose follow-up is deferred.
    /// </summary>
    public required IReadOnlyList<string> Issues { get; init; }

    public required IReadOnlyList<string> PendingReindex { get; init; }

    public string? ReindexJobId { get; init; }

    public string? ReindexStatusUrl { get; init; }
}
