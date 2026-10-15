// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>How current the data behind a response is.</summary>
/// <param name="IndexBuiltAt">When the catalog or index the response was answered from finished building.</param>
/// <param name="StaleFiles">Files changed since the index was built; not yet tracked, so always null in this prototype.</param>
public sealed record FreshnessInfo(DateTimeOffset IndexBuiltAt, int? StaleFiles);
