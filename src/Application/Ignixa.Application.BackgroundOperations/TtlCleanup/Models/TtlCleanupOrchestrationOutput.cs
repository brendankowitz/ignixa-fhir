// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Models;

/// <summary>
/// Output from the TTL cleanup orchestration.
/// </summary>
/// <param name="Success">Whether the cleanup completed successfully.</param>
/// <param name="TotalExpired">Total number of expired resources found across all tenants.</param>
/// <param name="TotalDeleted">Total number of resources successfully deleted.</param>
/// <param name="TotalFailed">Total number of resources that failed to delete.</param>
/// <param name="TenantResults">Per-tenant cleanup results.</param>
/// <param name="ErrorMessage">Error message if cleanup failed.</param>
public record TtlCleanupOrchestrationOutput(
    bool Success,
    int TotalExpired,
    int TotalDeleted,
    int TotalFailed,
    IReadOnlyList<TtlCleanupActivityOutput>? TenantResults,
    string? ErrorMessage);
