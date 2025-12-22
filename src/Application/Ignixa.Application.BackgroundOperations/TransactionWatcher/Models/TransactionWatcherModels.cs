// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;

/// <summary>
/// Input for the TransactionWatcher orchestration.
/// </summary>
/// <param name="ScanInterval">How frequently to scan for stalled transactions.</param>
/// <param name="StallThreshold">How old a transaction must be to be considered stalled.</param>
/// <param name="Enabled">Whether the transaction watcher is enabled.</param>
public record TransactionWatcherOrchestrationInput(
    TimeSpan ScanInterval,
    TimeSpan StallThreshold,
    bool Enabled);

/// <summary>
/// Output for the TransactionWatcher orchestration.
/// Since this is an eternal orchestration, it will only complete if disabled or on error.
/// </summary>
/// <param name="TotalScans">Total number of scan cycles completed.</param>
/// <param name="TotalCommitted">Total number of stalled transactions committed.</param>
/// <param name="TotalFailed">Total number of commit failures.</param>
/// <param name="StoppedReason">Reason the orchestration stopped (if applicable).</param>
public record TransactionWatcherOrchestrationOutput(
    int TotalScans,
    int TotalCommitted,
    int TotalFailed,
    string? StoppedReason);

/// <summary>
/// Input for the TransactionWatcher activity that performs a single scan cycle.
/// </summary>
/// <param name="StallThreshold">How old a transaction must be to be considered stalled.</param>
public record TransactionWatcherActivityInput(
    TimeSpan StallThreshold);

/// <summary>
/// Output from a single TransactionWatcher scan cycle.
/// </summary>
/// <param name="TenantsScanned">Number of tenants that were scanned.</param>
/// <param name="StalledTransactionsFound">Number of stalled transactions found across all tenants.</param>
/// <param name="TransactionsCommitted">Number of stalled transactions successfully committed.</param>
/// <param name="TransactionsFailed">Number of transactions that failed to commit.</param>
/// <param name="ScanDurationMs">Duration of the scan in milliseconds.</param>
public record TransactionWatcherActivityOutput(
    int TenantsScanned,
    int StalledTransactionsFound,
    int TransactionsCommitted,
    int TransactionsFailed,
    double ScanDurationMs);
