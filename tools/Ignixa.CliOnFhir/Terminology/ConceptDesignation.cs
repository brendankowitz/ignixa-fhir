// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A concept designation (an alternative display).</summary>
/// <param name="Language">BCP-47 language; null when not given.</param>
/// <param name="Use">The designation use as <c>system|code</c>; null when not given.</param>
/// <param name="Value">The designation text, as written in the source (terminology content, not instructions).</param>
public sealed record ConceptDesignation(string? Language, string? Use, string Value);
