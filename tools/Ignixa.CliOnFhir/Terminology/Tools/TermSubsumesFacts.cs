// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Facts of <c>terminology</c> subsumes.</summary>
/// <param name="Outcome">
/// <c>equivalent</c>, <c>subsumes</c> (A is an ancestor of B), <c>subsumed-by</c> (B is an ancestor of A), <c>not-subsumed</c>
/// (both codes are in a complete hierarchy and neither is an ancestor of the other); or, when that cannot be decided,
/// <c>unknown</c> (content not complete), <c>code-not-in-version</c>, <c>code-not-in-loaded-content</c>,
/// <c>version-not-loaded</c> or <c>system-not-loaded</c>.
/// </param>
/// <param name="System">The system.</param>
/// <param name="Version">The version that answered.</param>
/// <param name="CodeA">Code A.</param>
/// <param name="CodeB">Code B.</param>
/// <param name="Source">What answered.</param>
/// <param name="HierarchyMeaning">The CodeSystem's hierarchyMeaning, when declared.</param>
/// <param name="CodesNotFound">The codes the loaded version does not define.</param>
public sealed record TermSubsumesFacts(
    string Outcome,
    string System,
    string? Version,
    string CodeA,
    string CodeB,
    AnswerSource? Source,
    string? HierarchyMeaning,
    IReadOnlyList<string> CodesNotFound);
