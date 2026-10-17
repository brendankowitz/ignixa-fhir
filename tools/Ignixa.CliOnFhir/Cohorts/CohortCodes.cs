// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.DrugClasses;
using Ignixa.CliOnFhir.Terminology.Remote;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>
/// The codes each criterion is searched with. A Condition ValueSet is expanded as <c>terminology expand scope=folder</c> does
/// (loaded content, else <c>--tx-server</c>) and a drug class read from RxClass as <c>class-members</c> does; of either, only
/// the codes the folder's resources carry are searched, so the query stays small whatever the hierarchy's size.
/// </summary>
internal sealed class CohortCodes(FolderSession session, List<IssueItem> issues, List<OmittedItem> omitted)
{
    private TerminologyState? _terminology;

    /// <exception cref="ToolRequestException">A ValueSet that cannot be expanded, or a terminology server failure.</exception>
    public async Task<IReadOnlyList<CriterionCodes>> ConditionsAsync(IReadOnlyList<ConditionCriterion> criteria, CancellationToken cancellationToken)
    {
        List<CriterionCodes> resolved = [];
        for (int index = 0; index < criteria.Count; index++)
        {
            ConditionCriterion criterion = criteria[index];
            if (criterion.ValueSet is not { } valueSet)
            {
                resolved.Add(new CriterionCodes(criterion.Codes, null));
                continue;
            }

            IReadOnlyList<CohortCode> found = await ValueSetCodesAsync($"conditions[{index}]", TermExpand.Canonical(valueSet), cancellationToken);
            resolved.Add(new CriterionCodes([.. criterion.Codes.Concat(found).Distinct()], found));
        }

        return resolved;
    }

    /// <exception cref="ToolRequestException">A class asked without <c>--rxclass-server</c>, or an RxClass failure.</exception>
    public async Task<IReadOnlyList<CriterionCodes>> MedicationsAsync(IReadOnlyList<MedicationCriterion> criteria, CancellationToken cancellationToken)
    {
        List<CriterionCodes> resolved = [];
        for (int index = 0; index < criteria.Count; index++)
        {
            MedicationCriterion criterion = criteria[index];
            if (criterion is not { ClassId: { } classId, ClassSource: { } source })
            {
                resolved.Add(new CriterionCodes(criterion.Codes, null));
                continue;
            }

            IReadOnlyList<CohortCode> found = await ClassCodesAsync($"medications[{index}]", classId, source, cancellationToken);
            resolved.Add(new CriterionCodes([.. criterion.Codes.Concat(found).Distinct()], found));
        }

        return resolved;
    }

    private async Task<IReadOnlyList<CohortCode>> ValueSetCodesAsync(string where, string valueSet, CancellationToken cancellationToken)
    {
        TerminologyState state = await TerminologyAsync(cancellationToken);
        CanonicalResolution<ValueSetEntry> resolution = state.Store.ResolveValueSet(valueSet);
        TermExpandInFolder.Expansion expansion = await TermExpandInFolder.ReadAsync(session, state.Store, valueSet, resolution, filter: null, cancellationToken)
            ?? throw ToolRequestException.Error(
                resolution.LoadedVersions.Count > 0 ? TermExpand.VersionNotLoaded : TermExpand.ValueSetNotLoaded,
                $"{where}.valueSet {valueSet} is not loaded and no terminology server is configured: a SNOMED CT hierarchy (isa/<sctid>) needs the server started with --tx-server (for example https://tx.fhir.org/r4); otherwise list the codes.");

        issues.AddRange(expansion.Issues.Where(issue => !issues.Contains(issue)));
        omitted.AddRange(expansion.Omitted.Select(item => item with { What = $"{where}.valueSet: {item.What}" }));
        (List<TermFolderCode> inFolder, _) = TermExpandInFolder.InFolderCodes(session, expansion);
        return Searchable(where, [.. inFolder.Select(code => (code.System, code.Code))]);
    }

    private async Task<IReadOnlyList<CohortCode>> ClassCodesAsync(string where, string classId, DrugClassSource source, CancellationToken cancellationToken)
    {
        RxClassClient client = session.Terminology.Options.RxClass ?? throw ToolRequestException.Error(
            DrugClassCall.NotEnabledCode,
            $"{where}.classId needs NLM RxClass, which this session does not use: start the server with --rxclass-server https://rxnav.nlm.nih.gov/REST, or list the RxNorm codes.");
        var reads = new RxClassReads(client, session.Terminology.Overlap);
        List<DrugClassMember> members = await TermClassMembers.ReadMembersAsync(reads, source, classId, cancellationToken);
        issues.AddRange(reads.Issues().Where(issue => !issues.Contains(issue)));
        if (members.Count == 0)
        {
            issues.Add(new IssueItem("warning", TermClassMembers.EmptyClassCode, $"{where}: RxClass lists no members for {source.Name} class {classId}, so no order matches it; ids are case-sensitive (find them with terminology class-search).", Location: null));
        }

        if (!source.HasProducts)
        {
            issues.Add(new IssueItem("warning", TermClassMembers.IngredientsOnlyCode, $"{where}: {source.Name} classes list ingredients only, while orders usually carry product codes, so orders may be missed; ATC and VA list products.", Location: null));
        }

        IReadOnlyDictionary<string, int> folderCodes = FolderTerminology.ResourcesPerCode(session, TermClassMembers.RxNorm, TermClassMembers.MedicationTypes);
        return Searchable(where, [.. members
            .Where(member => folderCodes.ContainsKey(member.Rxcui))
            .OrderByDescending(member => folderCodes[member.Rxcui])
            .ThenBy(member => member.Rxcui, StringComparer.Ordinal)
            .Select(member => (TermClassMembers.RxNorm, member.Rxcui))]);
    }

    private IReadOnlyList<CohortCode> Searchable(string where, IReadOnlyList<(string System, string Code)> codes)
    {
        List<CohortCode> searchable = [.. codes.Select(code => CohortCode.From(code.System, code.Code)).OfType<CohortCode>().Distinct()];
        if (searchable.Count < codes.Count)
        {
            issues.Add(new IssueItem("warning", "code-not-searchable", $"{where}: {codes.Count - searchable.Count} codes hold a character a search cannot carry and were left out.", Location: null));
        }

        return searchable;
    }

    private async Task<TerminologyState> TerminologyAsync(CancellationToken cancellationToken)
    {
        if (_terminology is { } loaded)
        {
            return loaded;
        }

        TerminologyState state = await session.Terminology.GetAsync(cancellationToken);
        if (state.IsUnusable)
        {
            throw ToolRequestException.Error(TerminologyState.NothingLoadedCode, $"No terminology could be loaded, so a valueSet cannot be expanded: {string.Join(" ", state.LoadIssues.Take(3).Select(issue => issue.Diagnostics))}");
        }

        issues.AddRange(state.LoadIssues.Where(issue => !issues.Contains(issue)));
        _terminology = state;
        return state;
    }
}
