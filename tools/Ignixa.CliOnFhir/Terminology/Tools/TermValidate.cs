// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// <c>terminology_validate_code</c> / <c>cli-on-fhir term validate</c>: whether a code is in a CodeSystem or a ValueSet, and
/// whether a display matches. Only a complete answer is <c>valid</c> or <c>invalid</c>; anything the loaded terminology
/// cannot decide is <c>unknown</c> with the reason, never <c>invalid</c>.
/// </summary>
public static class TermValidate
{
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string Unknown = "unknown";

    public static Task<ToolEnvelope<TermValidateFacts?>> ValidateAsync(FolderSession session, string? code, string? system, string? display, string? valueSet, string? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync(
            session,
            state =>
            {
                string checkedCode = TermArguments.Required(code, "code");
                string? checkedSystem = TermArguments.Optional(system, "system");
                string? checkedValueSet = TermArguments.Optional(valueSet, "valueSet");
                if (checkedSystem is null && checkedValueSet is null)
                {
                    throw ToolRequestException.Error("invalid-parameter", "Give 'system' (to check the code against that CodeSystem), 'valueSet', or both.");
                }

                return checkedValueSet is null
                    ? AgainstSystem(state.Store, checkedSystem!, checkedCode, TermArguments.Optional(display, "display"), TermArguments.Optional(version, "version"))
                    : AgainstValueSet(state.Store, checkedValueSet, checkedSystem, checkedCode, TermArguments.Optional(display, "display"), TermArguments.Optional(version, "version"));
            },
            cancellationToken);
    }

    internal static TerminologyAnswer<TermValidateFacts> AgainstSystem(TerminologyStore store, string system, string code, string? display, string? version)
    {
        ConceptQuery query = ConceptQuery.Find(store, system, version, code);
        List<IssueItem> issues = [.. query.Issues(store, hierarchy: false)];
        (string result, string reason) = query.Outcome switch
        {
            ConceptQuery.Found => (Valid, "code-in-system"),
            ConceptQuery.CodeNotInVersion => (Invalid, ConceptQuery.CodeNotInVersion),
            _ => (Unknown, query.Outcome),
        };

        (bool? matches, string? shown) = DisplayCheck(query.Concept, display, query.Entry, issues);
        var facts = new TermValidateFacts(result, reason, system, query.Entry?.Version ?? version, query.Concept?.Code ?? code, null, null, query.Source(), shown, display, matches, query.Concept?.Inactive);
        return new TerminologyAnswer<TermValidateFacts>(facts, query.Evidence(), [], query.Absent(store) is { } absent ? [absent] : [], issues);
    }

    internal static TerminologyAnswer<TermValidateFacts> AgainstValueSet(TerminologyStore store, string valueSet, string? system, string code, string? display, string? version)
    {
        CanonicalResolution<ValueSetEntry> resolution = store.ResolveValueSet(valueSet);
        if (resolution.Entry is not { } entry)
        {
            var notLoaded = new TermValidateFacts(Unknown, TermExpand.ValueSetNotLoaded, system, version, code, resolution.Url, resolution.Version, null, null, display, null, null);
            return new TerminologyAnswer<TermValidateFacts>(notLoaded, [], [], [TermExpand.NotLoadedAbsent(store, resolution)], []);
        }

        ValueSetExpansion expansion = new ValueSetExpander(store).Expand(entry);
        List<IssueItem> issues = [.. store.ConflictIssues("ValueSet", entry.Url, entry.Version)];
        List<ExpansionCode> found = [.. expansion.Matching(system, code).Where(candidate => version is null || candidate.Version is null || candidate.Version == version)];
        List<EvidenceItem> evidence = [.. expansion.Used.Select(origin => origin.ToEvidence()).Distinct().Take(20)];
        if (found.Count > 1)
        {
            issues.Add(new IssueItem("warning", "system-ambiguous", $"{TerminologyNames.Code(code)} is in ValueSet {TerminologyNames.Canonical(entry.Url, entry.Version)} under {found.Count} systems; give 'system' to say which.", entry.Origin.ToEvidence().File));
            var ambiguous = new TermValidateFacts(Unknown, "system-ambiguous", system, null, code, entry.Url, entry.Version, AnswerSource.Local(entry.Origin), null, display, null, null);
            return new TerminologyAnswer<TermValidateFacts>(ambiguous, evidence, [], [], issues);
        }

        if (found.Count == 0)
        {
            if (!expansion.IsComplete)
            {
                issues.Add(TermExpand.IncompleteIssue(expansion));
                issues.AddRange(expansion.Incomplete);
            }

            string result = expansion.IsComplete ? Invalid : Unknown;
            string reason = expansion.IsComplete ? "not-in-value-set" : ValueSetExpansion.IncompleteCode;
            var missing = new TermValidateFacts(result, reason, system, version, code, entry.Url, entry.Version, AnswerSource.Local(entry.Origin), null, display, null, null);
            var absent = new AbsentItem(
                system is null ? TerminologyNames.Code(code) : TerminologyNames.Coded(system, code),
                $"the {expansion.Codes.Count} codes of ValueSet {TerminologyNames.Canonical(entry.Url, entry.Version)} expanded over {store.Describe()}{(expansion.IsComplete ? string.Empty : " (incomplete)")}",
                store.BuiltAt);
            return new TerminologyAnswer<TermValidateFacts>(missing, evidence, [], [absent], issues);
        }

        ExpansionCode member = found[0];
        if (!expansion.ExclusionsComplete)
        {
            issues.Add(TermExpand.IncompleteIssue(expansion));
            issues.AddRange(expansion.Incomplete);
            var undecided = new TermValidateFacts(Unknown, ValueSetExpansion.IncompleteCode, member.System, member.Version, member.Code, entry.Url, entry.Version, AnswerSource.Local(entry.Origin), member.Display, display, null, member.Inactive);
            return new TerminologyAnswer<TermValidateFacts>(undecided, evidence, [], [], issues);
        }

        Concept? concept = store.ResolveCodeSystem(member.System, member.Version).Entry?.Find(member.Code);
        (bool? matches, string? shown) = concept is not null
            ? DisplayCheck(concept, display, store.ResolveCodeSystem(member.System, member.Version).Entry, issues)
            : (display is null || member.Display is null ? null : Same(display, member.Display), member.Display);
        if (matches == false && concept is null)
        {
            issues.Add(DisplayMismatch(member.System, member.Code, null));
        }

        var facts = new TermValidateFacts(Valid, "code-in-value-set", member.System, member.Version, member.Code, entry.Url, entry.Version, AnswerSource.Local(entry.Origin), shown, display, matches, member.Inactive);
        return new TerminologyAnswer<TermValidateFacts>(facts, evidence, [], [], issues);
    }

    private static (bool? Matches, string? Display) DisplayCheck(Concept? concept, string? display, CodeSystemEntry? entry, List<IssueItem> issues)
    {
        if (concept is null || entry is null)
        {
            return (null, null);
        }

        if (concept.Inactive)
        {
            issues.Add(new IssueItem("warning", "inactive-concept", $"{TerminologyNames.Coded(entry.Url, concept.Code)} is inactive in {TerminologyNames.Canonical(entry.Url, entry.Version)}.", entry.Origin.ToEvidence().File));
        }

        if (display is null)
        {
            return (null, concept.Display);
        }

        bool matches = (concept.Display is { } own && Same(display, own)) || concept.Designations.Any(designation => Same(display, designation.Value));
        if (!matches)
        {
            issues.Add(DisplayMismatch(entry.Url, concept.Code, entry));
        }

        return (matches, concept.Display);
    }

    private static IssueItem DisplayMismatch(string system, string code, CodeSystemEntry? entry) => new(
        "warning",
        "display-mismatch",
        $"The display given for {TerminologyNames.Coded(system, code)} matches neither its display nor any designation{(entry is null ? string.Empty : $" in {TerminologyNames.Canonical(entry.Url, entry.Version)}")}; 'facts.display' has the expected one.",
        entry?.Origin.ToEvidence().File);

    private static bool Same(string left, string right) => string.Equals(TextFolding.Fold(left), TextFolding.Fold(right), StringComparison.Ordinal);
}
