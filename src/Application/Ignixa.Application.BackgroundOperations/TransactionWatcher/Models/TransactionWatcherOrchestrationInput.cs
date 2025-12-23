// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;

/// <summary>
/// Input for the TransactionWatcher orchestration.
/// Uses eternal orchestration pattern - runs forever with periodic scan cycles.
/// </summary>
/// <param name="StallThreshold">How old a transaction must be to be considered stalled. Default: 5 minutes.</param>
/// <param name="ScanInterval">Time to wait between scan cycles. Default: 60 seconds.</param>
public record TransactionWatcherOrchestrationInput(
    TimeSpan? StallThreshold = null,
    TimeSpan? ScanInterval = null)
{
    /// <summary>
    /// Gets the stall threshold, defaulting to 5 minutes if not specified.
    /// </summary>
    public TimeSpan EffectiveStallThreshold => StallThreshold ?? TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the scan interval, defaulting to 60 seconds if not specified.
    /// </summary>
    public TimeSpan EffectiveScanInterval => ScanInterval ?? TimeSpan.FromSeconds(60);
}
