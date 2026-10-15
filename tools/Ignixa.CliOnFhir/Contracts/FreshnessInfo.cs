// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>How current the data behind a response is.</summary>
/// <param name="IndexBuiltAt">When the catalog or index the response was answered from finished building.</param>
/// <param name="StaleFiles">
/// Files that differ from what the answer was built on. Zero whenever the catalog was walked or the persisted
/// index was validated against the files in this process; a rebuild forced by changed files reports the count
/// as an <c>index-cache-rebuilt</c> issue instead. Changes after the walk are not tracked.
/// </param>
public sealed record FreshnessInfo(DateTimeOffset IndexBuiltAt, int? StaleFiles);
