// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Search;

/// <summary>A token value the folder's index holds for a parameter, and how many resources hold it.</summary>
/// <param name="Value">
/// <c>system|code</c>, or <c>|code</c> for a code with no system, as a search value matches it; a system or code that is not
/// shaped like one is shown as its digest (<c>~</c> and 16 hex digits), which a search cannot use.
/// </param>
/// <param name="Resources">Resources with at least one entry of this value.</param>
public sealed record TopValue(string Value, int Resources);
