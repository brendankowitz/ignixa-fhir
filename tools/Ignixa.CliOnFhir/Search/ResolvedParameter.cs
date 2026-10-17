// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Search;

/// <summary>One search parameter whose relative date values (<c>ge now-7d</c>) were resolved against asOf before the search ran.</summary>
/// <param name="Parameter">The parameter as written (a known date parameter, with its chain).</param>
/// <param name="Original">Its value as written, every OR value with its prefix.</param>
/// <param name="Resolved">The value the search ran with: each relative value replaced by an absolute date or instant.</param>
public sealed record ResolvedParameter(string Parameter, string Original, string Resolved);
