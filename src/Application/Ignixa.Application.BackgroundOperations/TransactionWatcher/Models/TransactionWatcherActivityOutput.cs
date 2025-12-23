// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;

/// <summary>
/// Output from the TransactionWatcher activity (single tenant).
/// Contains results of scanning and committing stalled transactions for one tenant.
/// </summary>
/// <param name="TenantId">Tenant ID that was scanned.</param>
/// <param name="StalledCount">Number of stalled transactions found.</param>
/// <param name="CommittedCount">Number of transactions successfully committed.</param>
/// <param name="FailedCount">Number of transactions that failed to commit.</param>
/// <param name="ErrorMessage">Error message if the activity failed.</param>
public record TransactionWatcherActivityOutput(
    int TenantId,
    int StalledCount,
    int CommittedCount,
    int FailedCount,
    string? ErrorMessage);
