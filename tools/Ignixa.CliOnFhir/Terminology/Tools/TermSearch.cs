// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary><c>terminology_search</c> / <c>cli-on-fhir term search</c>: text to candidate codes over displays and designations.</summary>
public static class TermSearch
{
    public const string Searched = "searched";

    private const int MaxEvidence = 20;

    public static Task<ToolEnvelope<TermSearchFacts?>> SearchAsync(FolderSession session, string? text, string? system, string? valueSet, int? count, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync(
            session,
            state => Answer(state.Store, TermArguments.Required(text, "text"), TermArguments.Optional(system, "system"), TermArguments.Optional(valueSet, "valueSet"), count, offset, cancellationToken),
            cancellationToken);
    }

    private static TerminologyAnswer<TermSearchFacts> Answer(TerminologyStore store, string text, string? system, string? valueSet, int? count, int offset, CancellationToken cancellationToken)
    {
        List<IssueItem> issues = [];
        int pageSize = Paging.Resolve(count, offset, issues);
        string folded = TextFolding.Fold(text);
        if (folded.Length == 0)
        {
            throw ToolRequestException.Error("invalid-parameter", "'text' has nothing to match once whitespace is removed.");
        }

        (List<SearchCandidate> candidates, int systems, string outcome, AbsentItem? notLoaded, bool? complete, List<EvidenceItem> sourceEvidence) = Candidates(store, system, valueSet, issues, cancellationToken);
        List<(SearchCandidate Candidate, int Rank, string MatchedOn, string? MatchedText)> matches = [];
        foreach (SearchCandidate candidate in candidates)
        {
            if (Score(candidate, folded) is { } score)
            {
                matches.Add((candidate, score.Rank, score.On, score.Text));
            }
        }

        List<TermMatch> ranked = [.. matches
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Candidate.Display?.Length ?? int.MaxValue)
            .ThenBy(match => match.Candidate.SourceRank)
            .ThenBy(match => match.Candidate.System, StringComparer.Ordinal)
            .ThenBy(match => match.Candidate.Code, StringComparer.Ordinal)
            .Select(match => new TermMatch(
                match.Candidate.System,
                match.Candidate.Version,
                match.Candidate.Code,
                match.Candidate.Display is { } display ? display[..Math.Min(display.Length, TermText.MaxShortText)] : null,
                match.MatchedOn,
                match.Rank switch { 0 => "exact", 1 => "prefix", _ => "contains" },
                match.MatchedText is { } matched ? matched[..Math.Min(matched.Length, TermText.MaxShortText)] : null,
                match.Candidate.Inactive,
                match.Candidate.Source))];

        List<OmittedItem> omitted = [];
        List<TermMatch> page = [.. ranked.Skip(offset).Take(pageSize)];
        (TermSearchFacts facts, int kept) = TermText.FitPage(
            page,
            items => new TermSearchFacts(text, system, valueSet, outcome, systems, candidates.Count, complete, ranked.Count, offset, items.Count, items),
            "matches",
            omitted);
        issues.AddRange(Paging.BeyondTotal(offset, ranked.Count, "matches"));

        List<NextCall> next = [];
        int remaining = ranked.Count - offset - kept;
        if (remaining > 0)
        {
            omitted.Add(new OmittedItem($"{remaining} more matches", "limit"));
            var args = new Dictionary<string, object> { ["text"] = text, ["offset"] = offset + kept, ["count"] = pageSize };
            if (system is not null)
            {
                args["system"] = system;
            }

            if (valueSet is not null)
            {
                args["valueSet"] = valueSet;
            }

            if (TermNext.Echoable(system, valueSet))
            {
                next.Add(new NextCall("terminology_search", args));
            }
        }

        List<AbsentItem> absent = notLoaded is not null
            ? [notLoaded]
            : ranked.Count == 0
                ? [new AbsentItem("concepts matching the text", $"the displays and designations of {candidates.Count} concepts in {systems} code systems ({store.Describe()})", store.BuiltAt)]
                : [];

        HashSet<string> shownSystems = [.. facts.Matches.Select(match => match.System)];

        // Every CodeSystem a shown match came from answered: its conflicts apply (a ValueSet search already has them).
        if (valueSet is null)
        {
            issues.AddRange(facts.Matches
                .Select(match => (match.System, match.Version))
                .Distinct()
                .SelectMany(used => store.ConflictIssues("CodeSystem", used.System, used.Version))
                .Distinct());
        }

        List<EvidenceItem> evidence = [.. sourceEvidence
            .Concat(candidates.Where(candidate => candidate.Origin is not null && shownSystems.Contains(candidate.System)).Select(candidate => candidate.Origin!.ToEvidence()))
            .Distinct()
            .Take(MaxEvidence)];
        return new TerminologyAnswer<TermSearchFacts>(facts, evidence, omitted, absent, issues) { Next = next };
    }

    private static (int Rank, string On, string? Text)? Score(SearchCandidate candidate, string folded)
    {
        (int Rank, string On, string? Text)? best = Rank(candidate.FoldedDisplay, folded) is { } displayRank ? (displayRank, "display", null) : null;
        for (int index = 0; index < candidate.FoldedDesignations.Count; index++)
        {
            if (Rank(candidate.FoldedDesignations[index], folded) is { } rank && (best is null || rank < best.Value.Rank))
            {
                best = (rank, "designation", candidate.Designations[index]);
            }
        }

        return best;
    }

    private static int? Rank(string? value, string folded) => value switch
    {
        null => null,
        _ when value == folded => 0,
        _ when value.StartsWith(folded, StringComparison.Ordinal) => 1,
        _ when value.Contains(folded, StringComparison.Ordinal) => 2,
        _ => null,
    };

    private static (List<SearchCandidate> Candidates, int Systems, string Outcome, AbsentItem? NotLoaded, bool? Complete, List<EvidenceItem> Evidence) Candidates(TerminologyStore store, string? system, string? valueSet, List<IssueItem> issues, CancellationToken cancellationToken)
    {
        if (valueSet is not null)
        {
            CanonicalResolution<ValueSetEntry> resolution = store.ResolveValueSet(valueSet);
            if (resolution.Entry is not { } entry)
            {
                return ([], 0, TermExpand.ValueSetNotLoaded, TermExpand.NotLoadedAbsent(store, resolution), null, []);
            }

            ValueSetExpansion expansion = new ValueSetExpander(store).Expand(entry, cancellationToken);
            if (resolution.OtherVersions is { } otherValueSets)
            {
                issues.Add(otherValueSets);
            }

            issues.AddRange(store.ConflictIssues("ValueSet", entry.Url, entry.Version));
            if (!expansion.IsComplete)
            {
                issues.Add(TermExpand.IncompleteIssue(expansion));
                issues.AddRange(expansion.Incomplete);
            }

            // Includes the conflicts and other versions of the CodeSystems the expansion read.
            issues.AddRange(expansion.Notes);

            List<SearchCandidate> fromValueSet = [.. expansion.Codes
                .Where(code => system is null || code.System == system)
                .Select(code => SearchCandidate.From(store, code))];
            return (fromValueSet, fromValueSet.Select(candidate => candidate.System).Distinct(StringComparer.Ordinal).Count(), Searched, null, expansion.IsComplete, [entry.Origin.ToEvidence()]);
        }

        List<CodeSystemEntry> systems;
        if (system is not null)
        {
            CodeSystemResolution resolution = store.ResolveCodeSystem(system, version: null);
            if (resolution.Entry is not { } entry)
            {
                ConceptQuery query = new(system, null, string.Empty, resolution, null);
                return ([], 0, resolution.Outcome, query.Absent(store), null, []);
            }

            if (resolution.OtherVersions is { } otherVersions)
            {
                issues.Add(otherVersions);
            }

            systems = [entry];
        }
        else
        {
            systems = [.. store.CodeSystems
                .Where(entry => entry.ProvidesConcepts)
                .Select(entry => store.ResolveCodeSystem(entry.Url, version: null).Entry!)
                .Distinct()];
        }

        List<SearchCandidate> candidates = [.. systems.SelectMany(entry => entry.Folded.Select(concept => SearchCandidate.From(store, entry, concept)))];
        return (candidates, systems.Count, Searched, null, null, []);
    }
}
