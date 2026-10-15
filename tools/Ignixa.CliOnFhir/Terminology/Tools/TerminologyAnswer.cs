// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>What one terminology tool answered, before the shared envelope parts (load issues, freshness) are added.</summary>
internal sealed record TerminologyAnswer<TFacts>(
    TFacts Facts,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<OmittedItem> Omitted,
    IReadOnlyList<AbsentItem> Absent,
    IReadOnlyList<IssueItem> Issues)
{
    public IReadOnlyList<NextCall> Next { get; init; } = [];

    /// <summary>
    /// The load issues of the state the answer came from, when that is not the state the call started with (a package load
    /// publishes a new one); null means the starting state's.
    /// </summary>
    public IReadOnlyList<IssueItem>? LoadIssues { get; init; }
}
