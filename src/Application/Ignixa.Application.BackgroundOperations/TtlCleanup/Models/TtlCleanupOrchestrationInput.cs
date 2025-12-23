// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TtlCleanup.Models;

/// <summary>
/// Input for the TTL cleanup orchestration.
/// Uses eternal orchestration pattern - runs forever with periodic cleanup cycles.
/// </summary>
/// <param name="BatchSize">Maximum number of expired resources to process per tenant in a single run.</param>
/// <param name="ScanInterval">Time to wait between cleanup cycles. Default: 15 minutes.</param>
public record TtlCleanupOrchestrationInput(
    int BatchSize = 100,
    TimeSpan? ScanInterval = null)
{
    /// <summary>
    /// Gets the scan interval, defaulting to 15 minutes if not specified.
    /// </summary>
    public TimeSpan EffectiveScanInterval => ScanInterval ?? TimeSpan.FromMinutes(15);
}
