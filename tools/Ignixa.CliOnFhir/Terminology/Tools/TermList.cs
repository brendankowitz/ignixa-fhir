// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary><c>terminology_list</c> / <c>cli-on-fhir term list</c>: what is loaded and from where, and (on request) which folder codes resolve.</summary>
public static class TermList
{
    public const int FolderUsageLimit = 20;

    public static Task<ToolEnvelope<TermListFacts?>> ListAsync(FolderSession session, string? kind, string? package, string? filter, int? count, int offset, bool includeFolderUsage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync(session, state => Answer(session, state, kind, TermArguments.Optional(package, "package"), TermArguments.Optional(filter, "filter"), count, offset, includeFolderUsage), cancellationToken);
    }

    private static TerminologyAnswer<TermListFacts> Answer(FolderSession session, TerminologyState state, string? kind, string? package, string? filter, int? count, int offset, bool includeFolderUsage)
    {
        string listed = (kind?.Trim()) switch
        {
            null or "" or "codeSystems" => "codeSystems",
            "valueSets" => "valueSets",
            "conceptMaps" => "conceptMaps",
            _ => throw ToolRequestException.Error("invalid-parameter", "'kind' is 'codeSystems' (the default), 'valueSets' or 'conceptMaps'."),
        };

        List<IssueItem> issues = [];
        int pageSize = Paging.Resolve(count, offset, issues);
        TerminologyStore store = state.Store;
        if (package is not null && !store.Sources.Any(source => source.Info.Name == package))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'package' names no loaded source; the loaded ones are {(store.Sources.Count == 0 ? "none" : string.Join(", ", store.Sources.Select(source => source.Info.Name)))}.");
        }

        IEnumerable<TermResourceSummary> all = listed switch
        {
            "codeSystems" => store.CodeSystems.Select(TermResourceSummary.Of),
            "valueSets" => store.ValueSets.Select(entry => new TermResourceSummary(entry.Url, entry.Version, entry.Origin.Source.Name, null, null, null)),
            _ => store.ConceptMaps.Select(entry => new TermResourceSummary(entry.Url, entry.Version, entry.Origin.Source.Name, null, null, null)),
        };
        List<TermResourceSummary> items = [.. all.Where(item => (package is null || item.Source == package) && (filter is null || item.Url.Contains(filter, StringComparison.OrdinalIgnoreCase)))];

        List<OmittedItem> omitted = [];
        (TermFolderUsage? usage, OmittedItem? usageOmitted) = includeFolderUsage ? FolderTerminology.Compute(session, store, FolderUsageLimit) : (null, null);
        if (usageOmitted is not null)
        {
            omitted.Add(usageOmitted);
        }

        List<TermSourceSummary> sources = [.. store.Sources.Select(TermSourceSummary.Of)];
        List<TermResourceSummary> page = [.. items.Skip(offset).Take(pageSize)];
        (TermListFacts facts, int kept) = TermText.FitPage(
            page,
            shown => new TermListFacts(state.InitialLoadMilliseconds, state.HeapBytes, state.WorkingSetBytes, session.Terminology.Options.Offline, session.Terminology.Options.TxServer?.BaseUri.AbsoluteUri, sources, listed, package, filter, items.Count, offset, shown.Count, shown, usage),
            "resources",
            omitted);
        issues.AddRange(Paging.BeyondTotal(offset, items.Count, "resources"));

        List<NextCall> next = [];
        int remaining = items.Count - offset - kept;
        if (remaining > 0)
        {
            omitted.Add(new OmittedItem($"{remaining} more {listed}", "limit"));
            next.Add(new NextCall("terminology_list", Args(listed, package, filter, offset + kept, pageSize, includeFolderUsage)));
        }

        if (!includeFolderUsage)
        {
            next.Add(new NextCall("terminology_list", Args(listed, package, filter, offset, pageSize, includeFolderUsage: true)));
        }

        const int ConflictsListed = 5;
        issues.AddRange(store.Conflicts.Take(ConflictsListed).Select(conflict => conflict.ToIssue()));
        if (store.Conflicts.Count > ConflictsListed)
        {
            omitted.Add(new OmittedItem($"{store.Conflicts.Count - ConflictsListed} more canonical-conflict warnings (each answer that uses such a canonical reports it)", "limit"));
        }

        List<EvidenceItem> evidence = [.. store.Sources.Where(source => !source.Info.IsFolder).Select(source => new EvidenceItem(source.Info.Name, source.Info.EvidencePrefix + "package/package.json", null, 0))];
        return new TerminologyAnswer<TermListFacts>(facts, evidence, omitted, [], issues) { Next = next };
    }

    // Arguments are the caller's own filters (and loaded package names, which were checked as npm ids) plus paging.
    private static Dictionary<string, object> Args(string kind, string? package, string? filter, int offset, int count, bool includeFolderUsage)
    {
        var args = new Dictionary<string, object> { ["kind"] = kind, ["offset"] = offset, ["count"] = count };
        if (package is not null)
        {
            args["package"] = package;
        }

        if (filter is not null)
        {
            args["filter"] = filter;
        }

        if (includeFolderUsage)
        {
            args["includeFolderUsage"] = true;
        }

        return args;
    }
}
