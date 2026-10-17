// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>A validator issue of a draft, by rule and path, with the tool's own description of the rule.</summary>
/// <param name="Severity"><c>fatal</c>, <c>error</c>, <c>warning</c> or <c>information</c>.</param>
/// <param name="Profile">The StructureDefinition whose checks raised it.</param>
/// <param name="Rule">The rule (the validator's issue code, or the tool's name for a check that did not conclude).</param>
/// <param name="Constraint">For an invariant, its key.</param>
/// <param name="ValueSet">For a binding, the bound ValueSet.</param>
/// <param name="Path">The element, without indexes.</param>
/// <param name="Description">What the rule means, in the tool's words; null for a rule the tool does not describe.</param>
public sealed record DraftIssueGroup(string Severity, string Profile, string Rule, string? Constraint, string? ValueSet, string Path, string? Description);
