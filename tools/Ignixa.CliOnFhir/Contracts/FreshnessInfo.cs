// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>How current the data behind a response is.</summary>
/// <param name="IndexBuiltAt">When the catalog or index the response was answered from finished building.</param>
/// <param name="StaleFiles">
/// Files that differ from what the catalog (and so the index) was built on, counted afresh for each response:
/// scanned files modified or removed since the walk, plus supported files added since. An index loaded from the
/// persisted cache is only used when no file differed from the cache at load time, so this is what changed after.
/// Null when the folder could not be re-listed (deleted, unmounted, unreadable); a <c>freshness-unavailable</c> issue says why.
/// </param>
public sealed record FreshnessInfo(DateTimeOffset IndexBuiltAt, int? StaleFiles);
