// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A group's <c>unmapped</c> rule: what a source code without an element maps to.</summary>
/// <param name="Mode"><c>provided</c> (the same code), <c>fixed</c> (<paramref name="Code"/>), <c>other-map</c> (R4) or <c>use-source-code</c>/<c>other-map</c> (R5).</param>
/// <param name="Code">The fixed target code.</param>
/// <param name="Display">The fixed target display.</param>
/// <param name="OtherMap">The other ConceptMap's canonical for <c>other-map</c>.</param>
public sealed record ConceptMapUnmapped(string Mode, string? Code, string? Display, string? OtherMap);
