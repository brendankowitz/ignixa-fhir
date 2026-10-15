// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// One code looked up in the store. "Not found" has four distinct outcomes: the system is not loaded (including a
/// <c>content = not-present</c> stub such as SNOMED CT in the core package), the requested version is not loaded, the code
/// is not in the loaded version (authoritative, content complete), or the code is not in loaded content that is only a
/// fragment or example (not authoritative).
/// </summary>
internal sealed record ConceptQuery(string System, string? Version, string Code, CodeSystemResolution Resolution, Concept? Concept)
{
    public const string Found = "found";
    public const string CodeNotInVersion = "code-not-in-version";
    public const string CodeNotInLoadedContent = "code-not-in-loaded-content";

    public static ConceptQuery Find(TerminologyStore store, string system, string? version, string code)
    {
        ArgumentNullException.ThrowIfNull(store);

        CodeSystemResolution resolution = store.ResolveCodeSystem(system, version);
        return new ConceptQuery(system, version, code, resolution, resolution.Entry?.Find(code));
    }

    public CodeSystemEntry? Entry => Resolution.Entry;

    /// <summary><c>found</c>, <c>code-not-in-version</c>, <c>code-not-in-loaded-content</c>, <c>version-not-loaded</c> or <c>system-not-loaded</c>.</summary>
    public string Outcome => Concept is not null
        ? Found
        : Entry is { } entry ? (entry.IsComplete ? CodeNotInVersion : CodeNotInLoadedContent) : Resolution.Outcome;

    /// <summary>Whether the system (in the requested version) is not available locally, the case a terminology server may answer.</summary>
    public bool SystemMissing => Entry is null;

    /// <summary>The <c>absent</c> entry of a "not found", saying what was checked; null when found.</summary>
    public AbsentItem? Absent(TerminologyStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return Outcome switch
        {
            Found => null,
            CodeNotInVersion or CodeNotInLoadedContent => new AbsentItem(
                TerminologyNames.Coded(System, Code),
                $"CodeSystem {TerminologyNames.Canonical(Entry!.Url, Entry.Version)} from {Entry.Origin.Source.Name} (content '{TerminologyNames.Token(Entry.Content ?? "(none)")}', {Entry.InOrder.Count} concepts)"
                    + (Outcome == CodeNotInLoadedContent ? "; that content is not complete, so the code may still exist in the system" : string.Empty),
                store.BuiltAt),
            CodeSystemResolution.VersionNotLoaded => new AbsentItem(
                $"CodeSystem {TerminologyNames.Canonical(System, Version)}",
                $"the loaded versions of {TerminologyNames.Canonical(System, null)}: {string.Join(", ", Resolution.LoadedVersions.Select(entry => entry.Version is null ? "(no version)" : TerminologyNames.Version(entry.Version)))}",
                store.BuiltAt),
            _ => new AbsentItem(
                $"CodeSystem {TerminologyNames.Canonical(System, Version)}",
                store.Describe() + (Resolution.StubReason is { } stub ? $"; {stub}" : string.Empty),
                store.BuiltAt),
        };
    }

    /// <summary>The warnings an answer from <see cref="Entry"/> carries: canonical conflicts and, for hierarchy answers, defects.</summary>
    public IEnumerable<IssueItem> Issues(TerminologyStore store, bool hierarchy)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (Resolution.OtherVersions is { } others)
        {
            yield return others;
        }

        if (Entry is not { } entry)
        {
            yield break;
        }

        if (Concept is { } concept && entry.CaseSensitive is null && !string.Equals(concept.Code, Code, StringComparison.Ordinal))
        {
            yield return CaseInsensitiveMatch(entry, concept.Code);
        }

        foreach (IssueItem conflict in store.ConflictIssues("CodeSystem", entry.Url, entry.Version))
        {
            yield return conflict;
        }

        if (hierarchy && entry.HierarchyProblems is { Any: true } problems)
        {
            yield return new IssueItem(
                "warning",
                "hierarchy-defects",
                $"CodeSystem {TerminologyNames.Canonical(entry.Url, entry.Version)} from {entry.Origin.Source.Name} has {problems.DanglingEdges} parent or child references to undefined codes, {problems.CycleEdges} cyclic parent edges and {problems.DuplicateCodes} repeated codes; those edges are left out of its hierarchy.",
                entry.Origin.ToEvidence().File);
        }

        if (hierarchy && entry.HierarchyMeaning is { } meaning && meaning != "is-a")
        {
            yield return new IssueItem(
                "warning",
                "hierarchy-not-is-a",
                $"CodeSystem {TerminologyNames.Canonical(entry.Url, entry.Version)} declares hierarchyMeaning '{TerminologyNames.Token(meaning)}', so its hierarchy is not subsumption (is-a); read parents and children with that meaning.",
                entry.Origin.ToEvidence().File);
        }
    }

    /// <summary>The note for a code matched in another case in a CodeSystem that does not state whether it is case sensitive.</summary>
    public static IssueItem CaseInsensitiveMatch(CodeSystemEntry entry, string matched) => new(
        "information",
        "case-insensitive-match",
        $"The code was matched to {TerminologyNames.Coded(entry.Url, matched)} ignoring case: CodeSystem {TerminologyNames.Canonical(entry.Url, entry.Version)} does not state caseSensitive, so codes are accepted in any case (R4 CodeSystem.caseSensitive); produce the code as {TerminologyNames.Code(matched)}.",
        entry.Origin.ToEvidence().File);

    /// <summary>The answering CodeSystem's location; for a system only declared (a content-not-present stub), the stub's.</summary>
    public IReadOnlyList<EvidenceItem> Evidence() => Entry is { } entry
        ? [entry.Origin.ToEvidence()]
        : [.. Resolution.Stubs.Select(stub => stub.Origin.ToEvidence()).Distinct()];

    public AnswerSource? Source() => Entry is { } entry ? AnswerSource.Local(entry.Origin) : null;
}
