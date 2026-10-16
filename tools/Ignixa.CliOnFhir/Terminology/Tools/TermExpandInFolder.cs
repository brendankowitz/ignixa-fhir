// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Remote;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// <c>terminology</c> expand with <c>scope=folder</c> / <c>cli-on-fhir term expand --scope folder</c>: of a ValueSet's codes, only
/// those the folder's resources carry, each with how many resources carry it, and the searches that find those resources. It
/// answers "which codes of this hierarchy does the data use" (ICD-10 E11 as SNOMED CT <c>isa/44054006</c>) in one call.
/// </summary>
/// <remarks>
/// The whole expansion is read first: from loaded content when it expands completely, otherwise from the terminology server
/// (<c>--tx-server</c>), page by page, up to <see cref="MaxExpansionCodes"/> codes; reaching the cap is reported in
/// <c>omitted</c>, since codes beyond it were not compared. The folder side is the token search-index entries of every
/// resource type, as <see cref="FolderTerminology"/> collects them for class-members.
/// </remarks>
public static class TermExpandInFolder
{
    public const string FolderScope = "folder";

    /// <summary>Most codes read from an expansion; each is compared with the folder's codes.</summary>
    public const int MaxExpansionCodes = 10_000;

    /// <summary>Codes asked of the terminology server per <c>$expand</c> page.</summary>
    public const int ServerPageSize = 1_000;

    public const string Rule = "The ValueSet's expansion (loaded content when it expands completely, else the terminology server's, paged), kept when "
        + "a code is in a token search-index entry of any of the folder's resources (parameters over Identifier elements left out); resources counts "
        + "the resources carrying the code.";

    private const int DefaultCount = 50;
    private const int MaxCodesInSearch = 50;
    private const int MaxSearches = 2;
    private const int FactsBudget = 8 * 1024;

    public static Task<ToolEnvelope<TermFolderExpansionFacts?>> ExpandAsync(FolderSession session, string? valueSet, string? filter, int? count, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync<TermFolderExpansionFacts>(
            session,
            async state =>
            {
                string checkedValueSet = TermExpand.Canonical(TermArguments.Required(valueSet, "valueSet"));
                string? checkedFilter = TermArguments.Optional(filter, "filter");
                List<IssueItem> issues = [];
                int pageSize = Paging.Resolve(count ?? DefaultCount, offset, issues);
                CanonicalResolution<ValueSetEntry> resolution = state.Store.ResolveValueSet(checkedValueSet);
                LocalExpansion? local = resolution.Entry is { } entry ? TermExpand.ExpandLocally(state.Store, resolution, entry, checkedFilter, cancellationToken) : null;
                if (session.Terminology.Options.TxServer is { } tx && local is not { Expansion.IsComplete: true })
                {
                    string why = TxFallback.Why(
                        local is null ? [TermExpand.NotLoadedAbsent(state.Store, resolution)] : [],
                        local is null ? (resolution.LoadedVersions.Count > 0 ? TermExpand.VersionNotLoaded : TermExpand.ValueSetNotLoaded) : ValueSetExpansion.IncompleteCode);
                    Expansion remote = await ReadFromServerAsync(tx, checkedValueSet, checkedFilter, why, cancellationToken);
                    return InFolder(session, checkedValueSet, checkedFilter, remote, pageSize, offset, issues);
                }

                if (local is null)
                {
                    return NotLoaded(state.Store, resolution, checkedFilter, offset, issues);
                }

                var expansion = new Expansion(local.Entry.Url, local.Entry.Version, AnswerSource.Local(local.Entry.Origin), local.Expansion.IsComplete, local.Codes, TermExpand.Evidence(local.Expansion), local.Issues, []);
                return InFolder(session, checkedValueSet, checkedFilter, expansion, pageSize, offset, issues);
            },
            cancellationToken);
    }

    /// <summary>
    /// Every code of the server's expansion, page by page from offset 0 until the server has no more or
    /// <see cref="MaxExpansionCodes"/> are read; reaching the cap is an omitted item, since the codes after it were not compared.
    /// </summary>
    private static async Task<Expansion> ReadFromServerAsync(TxServerClient tx, string valueSet, string? filter, string why, CancellationToken cancellationToken)
    {
        List<ExpansionCode> codes = [];
        List<IssueItem> issues = [];
        List<OmittedItem> omitted = [];
        TermExpandFacts? first = null;
        while (true)
        {
            TerminologyAnswer<TermExpandFacts> page = await TxAnswers.ExpandAsync(tx, valueSet, filter, ServerPageSize, codes.Count, why, cancellationToken);
            first ??= page.Facts;
            codes.AddRange(page.Facts.Codes);
            issues.AddRange(page.Issues.Where(issue => !issues.Contains(issue)));
            if (page.Next.Count == 0 || page.Facts.Codes.Count == 0)
            {
                // Every page was read, so a page's unknown total no longer leaves codes unread.
                issues.RemoveAll(issue => issue.Code == TxAnswers.TotalUnknownCode);
                break;
            }

            if (codes.Count >= MaxExpansionCodes)
            {
                string beyond = page.Facts.Total > codes.Count ? $"{page.Facts.Total - codes.Count} more codes" : "possibly more codes";
                omitted.Add(new OmittedItem($"{beyond} of the expansion were not read (at most {MaxExpansionCodes} are compared with the folder), so folder codes among them are missing; expand a narrower ValueSet", "limit"));
                break;
            }
        }

        return new Expansion(first.ValueSet, first.Version, first.Source, Complete: null, codes, [], issues, omitted);
    }

    private static TerminologyAnswer<TermFolderExpansionFacts> InFolder(FolderSession session, string valueSet, string? filter, Expansion expansion, int pageSize, int offset, List<IssueItem> issues)
    {
        Dictionary<string, HashSet<string>> wanted = new(StringComparer.Ordinal);
        Dictionary<(string System, string Code), string?> displays = [];
        foreach (ExpansionCode code in expansion.Codes)
        {
            (wanted.TryGetValue(code.System, out HashSet<string>? set) ? set : wanted[code.System] = new HashSet<string>(StringComparer.Ordinal)).Add(code.Code);
            displays.TryAdd((code.System, code.Code), code.Display);
        }

        FolderCodeUses uses = FolderTerminology.Uses(session, wanted);
        List<TermFolderCode> inFolder = [.. uses.Codes
            .Select(use => new TermFolderCode(use.System, use.Code, Clip(displays.GetValueOrDefault((use.System, use.Code))), use.Resources))
            .OrderByDescending(code => code.Resources)
            .ThenBy(code => code.System, StringComparer.Ordinal)
            .ThenBy(code => code.Code, StringComparer.Ordinal)];

        List<OmittedItem> omitted = [.. expansion.Omitted];
        List<TermFolderCode> page = [.. inFolder.Skip(offset).Take(pageSize)];
        (TermFolderExpansionFacts facts, int kept) = TermText.FitPage(
            page,
            items => new TermFolderExpansionFacts(expansion.Url, expansion.Version, TermExpand.Expanded, FolderScope, expansion.Source, expansion.Complete, filter, Rule, expansion.Codes.Count, inFolder.Count, offset, items.Count, items),
            "codes",
            omitted,
            FactsBudget);

        issues.AddRange(expansion.Issues);
        issues.AddRange(Paging.BeyondTotal(offset, inFolder.Count, "folder codes"));
        List<NextCall> next = Searches(inFolder, uses.Searches);
        int remaining = inFolder.Count - offset - kept;
        if (remaining > 0)
        {
            omitted.Add(new OmittedItem($"{remaining} more folder codes", "limit"));
            if (TermNext.Echoable(valueSet))
            {
                var args = new Dictionary<string, object> { ["valueSet"] = valueSet, ["scope"] = FolderScope, ["offset"] = offset + kept, ["count"] = pageSize };
                if (filter is not null)
                {
                    args["filter"] = filter;
                }

                next.Add(TermNext.Call("expand", args));
            }
        }

        List<AbsentItem> absent = inFolder.Count == 0
            ? [new AbsentItem($"codes of ValueSet {TerminologyNames.Canonical(expansion.Url, expansion.Version)} in the folder", $"the {expansion.Codes.Count} codes of its expansion against the token search-index entries of every resource type", session.Catalog.BuiltAt)]
            : [];
        return new TerminologyAnswer<TermFolderExpansionFacts>(facts, expansion.Evidence, omitted, absent, issues) { Next = next };
    }

    private static TerminologyAnswer<TermFolderExpansionFacts> NotLoaded(TerminologyStore store, CanonicalResolution<ValueSetEntry> resolution, string? filter, int offset, List<IssueItem> issues)
    {
        TerminologyAnswer<TermExpandFacts> notLoaded = TermExpand.NotLoaded(store, resolution, filter, offset, issues);
        var facts = new TermFolderExpansionFacts(resolution.Url, resolution.Version, notLoaded.Facts.Outcome, FolderScope, null, null, filter, Rule, 0, 0, offset, 0, []);
        issues.Add(new IssueItem("information", "tx-server-not-enabled", "The ValueSet is not loaded and no terminology server is configured; a SNOMED CT, LOINC or RxNorm hierarchy (isa/<code>) needs the server started with --tx-server, for example https://tx.fhir.org/r4.", Location: null));
        return new TerminologyAnswer<TermFolderExpansionFacts>(facts, [], [], notLoaded.Absent, issues);
    }

    // Built from structure only: type and parameter names from the index, and the codes the folder itself holds.
    private static List<NextCall> Searches(List<TermFolderCode> codes, IReadOnlyList<FolderCodeSearch> searches)
    {
        if (codes.Count == 0)
        {
            return [];
        }

        string values = string.Join(',', codes.Take(MaxCodesInSearch).Select(code => $"{code.System}|{code.Code}"));
        string which = codes.Count > MaxCodesInSearch ? $"the {MaxCodesInSearch} most used of these codes" : $"these {codes.Count} codes";
        return [.. searches.Take(MaxSearches).Select(search => new NextCall(
            "search_fhir_resources",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["resourceType"] = search.ResourceType, ["query"] = $"{search.Parameter}={values}" },
            $"The {search.Resources} {search.ResourceType} resources with one of {which}; add clinical-status=active or summary=count, or use the query in patientSearch('{search.ResourceType}', ...) for a cohort."))];
    }

    private static string? Clip(string? display) => display is null ? null : display[..Math.Min(display.Length, TermText.MaxShortText)];

    /// <summary>
    /// An expansion as read: the ValueSet expanded and what answered, whether it is complete (null when a server answered, which
    /// does not say), every code, and what reading it reported.
    /// </summary>
    private sealed record Expansion(
        string Url,
        string? Version,
        AnswerSource? Source,
        bool? Complete,
        IReadOnlyList<ExpansionCode> Codes,
        IReadOnlyList<EvidenceItem> Evidence,
        IReadOnlyList<IssueItem> Issues,
        IReadOnlyList<OmittedItem> Omitted);
}
