// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>A ValueSet's expansion over loaded content, unpaged (<see cref="TermExpand.ExpandLocally"/>).</summary>
/// <param name="Entry">The ValueSet expanded.</param>
/// <param name="Expansion">The expansion, with what it read and the rules it could not evaluate.</param>
/// <param name="Codes">Its codes that pass the filter, in compose order.</param>
/// <param name="Issues">What the expansion raised: other versions loaded, incompleteness and its rules, notes, canonical conflicts.</param>
internal sealed record LocalExpansion(ValueSetEntry Entry, ValueSetExpansion Expansion, IReadOnlyList<ExpansionCode> Codes, IReadOnlyList<IssueItem> Issues);
