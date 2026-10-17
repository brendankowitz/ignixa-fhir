// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Clock;

/// <summary>Where an <see cref="AsOf"/> instant came from, as facts name it.</summary>
public static class AsOfSources
{
    /// <summary>The call's own <c>asOf</c> argument, which overrides the session's value for that call.</summary>
    public const string Argument = "argument";

    /// <summary>The session's <c>--as-of</c> option.</summary>
    public const string SessionOption = "session-option";

    /// <summary>The <c>transactionTime</c> of a Bulk Data export manifest at the folder's root.</summary>
    public const string BulkExportManifest = "bulk-export-manifest";

    /// <summary>The <c>timestamp</c> of the folder's container Bundles (the latest when they differ).</summary>
    public const string BundleTimestamp = "bundle-timestamp";

    /// <summary>The session's clock: the folder records no data-pull time.</summary>
    public const string Clock = "clock";
}
