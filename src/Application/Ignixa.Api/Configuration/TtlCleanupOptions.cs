// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.Configuration;

/// <summary>
/// Configuration options for the TTL Cleanup background service.
/// The TTL Cleanup service automatically deletes expired resources based on their ExpiresAt timestamp.
/// </summary>
public class TtlCleanupOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "TtlCleanup";

    /// <summary>
    /// Whether the TTL cleanup service is enabled.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How frequently to scan for expired resources.
    /// Default: 15 minutes.
    /// </summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Maximum number of resources to delete in a single scan cycle per tenant.
    /// Prevents overwhelming the database with large batch deletions.
    /// Default: 500.
    /// </summary>
    public int BatchSize { get; set; } = 500;
}
