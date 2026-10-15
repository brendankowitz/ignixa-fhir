// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One loaded ConceptMap version.</summary>
/// <param name="Url">The canonical url.</param>
/// <param name="Version">The business version; null when none.</param>
/// <param name="SourceScope">The source ValueSet canonical or uri (R4 source[x], R5 sourceScope[x]); null when none.</param>
/// <param name="TargetScope">The target ValueSet canonical or uri; null when none.</param>
/// <param name="Groups">The groups in source order.</param>
/// <param name="Origin">Where it was read from.</param>
/// <param name="Fingerprint">Hash of the groups, to tell a true canonical collision from a repeat.</param>
public sealed record ConceptMapEntry(
    string Url,
    string? Version,
    string? SourceScope,
    string? TargetScope,
    IReadOnlyList<ConceptMapGroup> Groups,
    ResourceOrigin Origin,
    string Fingerprint);
