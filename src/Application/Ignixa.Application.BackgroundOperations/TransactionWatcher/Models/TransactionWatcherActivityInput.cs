// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;

/// <summary>
/// Input for the TransactionWatcher activity.
/// Scans a single tenant for stalled transactions and commits them.
/// </summary>
/// <param name="TenantId">Tenant ID to scan.</param>
/// <param name="StallThreshold">How old a transaction must be to be considered stalled.</param>
public record TransactionWatcherActivityInput(
    int TenantId,
    TimeSpan StallThreshold);
