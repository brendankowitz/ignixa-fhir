// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Models;

/// <summary>
/// Output from the TTL cleanup activity (per-tenant).
/// </summary>
/// <param name="TenantId">The tenant that was processed.</param>
/// <param name="ExpiredCount">Number of expired resources found.</param>
/// <param name="DeletedCount">Number of resources successfully deleted.</param>
/// <param name="FailedCount">Number of resources that failed to delete.</param>
/// <param name="ErrorMessage">Error message if the activity failed.</param>
public record TtlCleanupActivityOutput(
    int TenantId,
    int ExpiredCount,
    int DeletedCount,
    int FailedCount,
    string? ErrorMessage);
