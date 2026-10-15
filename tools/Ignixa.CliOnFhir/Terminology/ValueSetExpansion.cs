// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A ValueSet expanded over the loaded content.</summary>
/// <param name="ValueSet">The ValueSet expanded.</param>
/// <param name="Codes">The codes, in compose order (first occurrence kept), without duplicates.</param>
/// <param name="Incomplete">
/// Why codes may be missing (or wrongly present): a rule over a system that is not loaded, a filter this store cannot
/// evaluate, content that is not complete, a ValueSet not loaded, a cycle. Empty when the expansion is complete.
/// </param>
/// <param name="Notes">Warnings and information that do not make the expansion incomplete.</param>
/// <param name="Used">The ValueSets and CodeSystems read, in first-use order.</param>
/// <param name="ExclusionsComplete">
/// Whether every exclude rule (here and in included ValueSets) was evaluated; when not, a code in <paramref name="Codes"/> may
/// still be excluded, so its presence is not proof of membership.
/// </param>
public sealed record ValueSetExpansion(
    ValueSetEntry ValueSet,
    IReadOnlyList<ExpansionCode> Codes,
    IReadOnlyList<IssueItem> Incomplete,
    IReadOnlyList<IssueItem> Notes,
    IReadOnlyList<ResourceOrigin> Used,
    bool ExclusionsComplete)
{
    public const string IncompleteCode = "expansion-incomplete";

    public bool IsComplete => Incomplete.Count == 0;

    /// <summary>The expansion's codes for <paramref name="code"/>, in <paramref name="system"/> when given.</summary>
    public IEnumerable<ExpansionCode> Matching(string? system, string code) =>
        Codes.Where(candidate => candidate.Code == code && (system is null || candidate.System == system));
}
