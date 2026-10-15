// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>The state of one ValueSet expansion while its rules are evaluated.</summary>
internal sealed class ExpansionContext(ValueSetEntry valueSet, IReadOnlyList<string> stack)
{
    public ValueSetEntry ValueSet { get; } = valueSet;

    public IReadOnlyList<string> Stack { get; } = stack;

    public ExpansionBuilder Result { get; } = new();

    public List<IssueItem> Incomplete { get; } = [];

    public List<IssueItem> Notes { get; } = [];

    public List<ResourceOrigin> Used { get; } = [];

    public bool ExclusionsComplete { get; set; } = true;

    public ValueSetExpansion Finish() => new(
        ValueSet,
        [.. Result.Codes],
        [.. Incomplete.Distinct()],
        [.. Notes.Distinct()],
        [.. Used.Distinct()],
        ExclusionsComplete);
}
