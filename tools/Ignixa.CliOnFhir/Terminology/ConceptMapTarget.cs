// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One <c>group.element.target</c>.</summary>
/// <param name="Code">The target code; null for an R4 <c>unmatched</c> target, which maps to nothing.</param>
/// <param name="Display">The target display (terminology content, not instructions).</param>
/// <param name="Equivalence">R4 <c>equivalence</c>; null when the map uses R5 <c>relationship</c>.</param>
/// <param name="Relationship">R5 <c>relationship</c>; null when the map uses R4 <c>equivalence</c>.</param>
/// <param name="Comment">The mapping comment as written (terminology content).</param>
/// <param name="Conditional">True when the target has <c>dependsOn</c> or <c>product</c>: the mapping holds only together with other elements.</param>
public sealed record ConceptMapTarget(string? Code, string? Display, string? Equivalence, string? Relationship, string? Comment, bool Conditional);
