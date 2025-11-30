// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.Configuration;

/// <summary>
/// Configuration options for the Transaction Watcher background service.
/// The Transaction Watcher monitors for stalled transactions and automatically commits them.
/// </summary>
public class TransactionWatcherOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "TransactionWatcher";

    /// <summary>
    /// Whether the transaction watcher is enabled.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How frequently to scan for stalled transactions.
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How old a transaction must be (based on last heartbeat or file modification time) to be considered stalled.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan StallThreshold { get; set; } = TimeSpan.FromMinutes(5);
}
