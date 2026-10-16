// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Validation;

/// <summary>One validator issue as a summary groups it, with nothing from the resource left in it.</summary>
/// <param name="Severity"><c>fatal</c>, <c>error</c>, <c>warning</c> or <c>information</c>.</param>
/// <param name="Profile">The StructureDefinition whose checks raised it: the core definition of the type, or a profile (shape-checked).</param>
/// <param name="Rule">The validator's issue code or constraint key, or a <see cref="ValidationRules"/> name for a check that did not conclude.</param>
/// <param name="Path">The element path without indexes (<see cref="IssuePath"/>).</param>
/// <param name="Message">The message template (<see cref="MessageTemplate"/>); null when too long to show.</param>
internal sealed record ClassifiedIssue(string Severity, string Profile, string Rule, string Path, string? Message)
{
    public bool Fails => Severity is "error" or "fatal";

    public bool Unevaluated => ValidationRules.Unevaluated.Contains(Rule);
}
