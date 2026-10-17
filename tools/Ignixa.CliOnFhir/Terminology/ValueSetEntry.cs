// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One loaded ValueSet version.</summary>
/// <param name="Url">The canonical url.</param>
/// <param name="Version">The business version; null when none.</param>
/// <param name="Includes">compose.include, in order.</param>
/// <param name="Excludes">compose.exclude, in order.</param>
/// <param name="IncludesInactive">compose.inactive; null when not stated (inactive concepts are then included).</param>
/// <param name="HasCompose">Whether the ValueSet has a compose; without one, <paramref name="StoredExpansion"/> is its only content.</param>
/// <param name="StoredExpansion">expansion.contains flattened, as the package shipped it; empty when none.</param>
/// <param name="Origin">Where it was read from.</param>
/// <param name="Fingerprint">Hash of compose and expansion.contains, to tell a true canonical collision from a repeat.</param>
/// <param name="VersionAlgorithm">R5 versionAlgorithm[x]; null when not declared.</param>
public sealed record ValueSetEntry(
    string Url,
    string? Version,
    IReadOnlyList<ValueSetRule> Includes,
    IReadOnlyList<ValueSetRule> Excludes,
    bool? IncludesInactive,
    bool HasCompose,
    IReadOnlyList<ExpansionCode> StoredExpansion,
    ResourceOrigin Origin,
    string Fingerprint,
    string? VersionAlgorithm = null);
