// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Validation;

/// <summary>One validator issue as a summary groups it. Nothing in it is validator text or resource content.</summary>
/// <param name="Severity"><c>fatal</c>, <c>error</c>, <c>warning</c> or <c>information</c>.</param>
/// <param name="Profile">The StructureDefinition whose checks raised it: the core definition of the type, or a profile (shape-checked).</param>
/// <param name="Rule">The validator's issue code (shape-checked), <see cref="ValidationRules.Invariant"/>, or a <see cref="ValidationRules"/> name for a check that did not conclude.</param>
/// <param name="Constraint">For an invariant or a constraint that could not be evaluated, its key (shape-checked); null otherwise.</param>
/// <param name="ValueSet">For a binding rule, the bound ValueSet (shape-checked); null otherwise.</param>
/// <param name="Path">The element path without indexes (<see cref="IssuePath"/>).</param>
internal sealed record ClassifiedIssue(string Severity, string Profile, string Rule, string? Constraint, string? ValueSet, string Path)
{
    public bool Fails => Severity is "error" or "fatal";

    public bool Unevaluated => ValidationRules.Unevaluated.Contains(Rule);
}
