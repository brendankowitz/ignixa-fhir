// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary><c>terminology_expand</c> / <c>cli-on-fhir term expand</c>: a ValueSet's codes over loaded content, filtered and paged.</summary>
public static class TermExpand
{
    public const string Expanded = "expanded";
    public const string ValueSetNotLoaded = "value-set-not-loaded";
    public const string VersionNotLoaded = "version-not-loaded";

    private const int MaxEvidence = 20;

    public static Task<ToolEnvelope<TermExpandFacts?>> ExpandAsync(FolderSession session, string? valueSet, string? filter, int? count, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync<TermExpandFacts>(
            session,
            async state =>
            {
                string checkedValueSet = TermArguments.Required(valueSet, "valueSet");
                string? checkedFilter = TermArguments.Optional(filter, "filter");
                TerminologyAnswer<TermExpandFacts> local = Answer(state.Store, checkedValueSet, checkedFilter, count, offset);
                bool missing = local.Facts.Outcome != Expanded || local.Facts.Complete == false;
                return session.Terminology.Options.TxServer is { } tx && missing
                    ? await TxAnswers.ExpandAsync(tx, checkedValueSet, checkedFilter, Patients.Paging.Resolve(count, offset, []), offset, TxFallback.Why(local.Absent, local.Facts.Outcome == Expanded ? ValueSetExpansion.IncompleteCode : local.Facts.Outcome), cancellationToken)
                    : local;
            },
            cancellationToken);
    }

    internal static AbsentItem NotLoadedAbsent(TerminologyStore store, CanonicalResolution<ValueSetEntry> resolution) => resolution.LoadedVersions.Count > 0
        ? new AbsentItem($"ValueSet {TerminologyNames.Canonical(resolution.Url, resolution.Version)}", $"the loaded versions of {TerminologyNames.Canonical(resolution.Url, null)}: {string.Join(", ", resolution.LoadedVersions.Select(version => version is null ? "(no version)" : TerminologyNames.Version(version)))}", store.BuiltAt)
        : new AbsentItem($"ValueSet {TerminologyNames.Canonical(resolution.Url, resolution.Version)}", store.Describe(), store.BuiltAt);

    internal static IssueItem IncompleteIssue(ValueSetExpansion expansion) => new(
        "warning",
        ValueSetExpansion.IncompleteCode,
        $"The expansion of ValueSet {TerminologyNames.Canonical(expansion.ValueSet.Url, expansion.ValueSet.Version)} over the loaded terminology is incomplete: {expansion.Incomplete.Count} rules could not be evaluated (the issues that follow say which), so codes may be missing from it.",
        expansion.ValueSet.Origin.ToEvidence().File);

    private static TerminologyAnswer<TermExpandFacts> Answer(TerminologyStore store, string valueSet, string? filter, int? count, int offset)
    {
        List<IssueItem> issues = [];
        int pageSize = Paging.Resolve(count, offset, issues);
        CanonicalResolution<ValueSetEntry> resolution = store.ResolveValueSet(valueSet);
        if (resolution.Entry is not { } entry)
        {
            string outcome = resolution.LoadedVersions.Count > 0 ? VersionNotLoaded : ValueSetNotLoaded;
            var notLoaded = new TermExpandFacts(resolution.Url, resolution.Version, outcome, null, null, filter, 0, offset, 0, [], outcome == VersionNotLoaded ? resolution.LoadedVersions : null);
            return new TerminologyAnswer<TermExpandFacts>(notLoaded, [], [], [NotLoadedAbsent(store, resolution)], issues);
        }

        ValueSetExpansion expansion = new ValueSetExpander(store).Expand(entry);
        if (!expansion.IsComplete)
        {
            issues.Add(IncompleteIssue(expansion));
            issues.AddRange(expansion.Incomplete);
        }

        issues.AddRange(expansion.Notes);
        issues.AddRange(store.ConflictIssues("ValueSet", entry.Url, entry.Version));

        string? folded = filter is null ? null : TextFolding.Fold(filter);
        List<ExpansionCode> codes = folded is null
            ? [.. expansion.Codes]
            : [.. expansion.Codes.Where(code => code.Code.StartsWith(filter!, StringComparison.Ordinal) || (code.Display is { } display && TextFolding.Fold(display).Contains(folded, StringComparison.Ordinal)))];

        List<OmittedItem> omitted = [];
        List<ExpansionCode> page = [.. codes.Skip(offset).Take(pageSize).Select(code => code with { Display = code.Display is { } display ? display[..Math.Min(display.Length, TermText.MaxShortText)] : null })];
        (TermExpandFacts facts, int kept) = TermText.FitPage(
            page,
            items => new TermExpandFacts(entry.Url, entry.Version, Expanded, AnswerSource.Local(entry.Origin), expansion.IsComplete, filter, codes.Count, offset, items.Count, items, null),
            "codes",
            omitted);
        issues.AddRange(Paging.BeyondTotal(offset, codes.Count, "codes"));

        List<NextCall> next = [];
        int remaining = codes.Count - offset - kept;
        if (remaining > 0)
        {
            omitted.Add(new OmittedItem($"{remaining} more codes", "limit"));
            var args = new Dictionary<string, object> { ["valueSet"] = valueSet, ["offset"] = offset + kept, ["count"] = pageSize };
            if (filter is not null)
            {
                args["filter"] = filter;
            }

            next.Add(new NextCall("terminology_expand", args));
        }

        List<AbsentItem> absent = codes.Count == 0
            ? [new AbsentItem($"codes of ValueSet {TerminologyNames.Canonical(entry.Url, entry.Version)}{(filter is null ? string.Empty : " matching the filter")}", $"its expansion over {store.Describe()}{(expansion.IsComplete ? string.Empty : " (incomplete)")}", store.BuiltAt)]
            : [];

        List<EvidenceItem> evidence = [.. expansion.Used.Select(origin => origin.ToEvidence()).Distinct()];
        if (evidence.Count > MaxEvidence)
        {
            omitted.Add(new OmittedItem($"{evidence.Count - MaxEvidence} more evidence entries (resources the expansion read)", "limit"));
            evidence = [.. evidence.Take(MaxEvidence)];
        }

        return new TerminologyAnswer<TermExpandFacts>(facts, evidence, omitted, absent, issues) { Next = next };
    }
}
