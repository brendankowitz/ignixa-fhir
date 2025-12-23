// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;

/// <summary>
/// Output from the TransactionWatcher orchestration (one cycle).
/// Contains aggregated results from all tenant activities.
/// </summary>
/// <param name="Success">Whether the scan cycle completed successfully.</param>
/// <param name="TotalStalled">Total number of stalled transactions found across all tenants.</param>
/// <param name="TotalCommitted">Total number of transactions successfully committed across all tenants.</param>
/// <param name="TotalFailed">Total number of transactions that failed to commit across all tenants.</param>
/// <param name="TenantResults">Per-tenant results from activities.</param>
/// <param name="ErrorMessage">Error message if the cycle failed.</param>
public record TransactionWatcherOrchestrationOutput(
    bool Success,
    int TotalStalled,
    int TotalCommitted,
    int TotalFailed,
    IReadOnlyList<TransactionWatcherActivityOutput> TenantResults,
    string? ErrorMessage);
