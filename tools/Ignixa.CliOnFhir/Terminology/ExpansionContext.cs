// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>The state of one ValueSet expansion while its rules are evaluated.</summary>
/// <param name="valueSet">The ValueSet being expanded.</param>
/// <param name="graph">The include graph of the whole request, which says which includes close a cycle.</param>
/// <param name="expanded">The expansions of the ValueSets this one includes, computed before it.</param>
/// <param name="cancellationToken">Cancels the request.</param>
internal sealed class ExpansionContext(
    ValueSetEntry valueSet,
    ValueSetIncludeGraph graph,
    IReadOnlyDictionary<ValueSetEntry, ValueSetExpansion> expanded,
    CancellationToken cancellationToken)
{
    public ValueSetEntry ValueSet { get; } = valueSet;

    public ValueSetIncludeGraph Graph { get; } = graph;

    public IReadOnlyDictionary<ValueSetEntry, ValueSetExpansion> Expanded { get; } = expanded;

    public CancellationToken CancellationToken { get; } = cancellationToken;

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
