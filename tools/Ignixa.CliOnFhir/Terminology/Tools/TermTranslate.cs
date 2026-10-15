// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// <c>terminology_translate</c> / <c>cli-on-fhir term translate</c>: source system and code to targets through the loaded
/// ConceptMaps (one named map, or every map with a group from the system), with R4 equivalence or R5 relationship.
/// A group's <c>unmapped</c> rule applies to codes it does not list: <c>provided</c> and <c>fixed</c> are answered, an
/// <c>other-map</c> is reported, not followed. Reverse translation and dependsOn conditions are not evaluated.
/// </summary>
public static class TermTranslate
{
    public const string Mapped = "mapped";
    public const string NotMapped = "not-mapped";
    public const string NoMapForSystem = "no-map-for-system";
    public const string ConceptMapNotLoaded = "concept-map-not-loaded";

    public static Task<ToolEnvelope<TermTranslateFacts?>> TranslateAsync(FolderSession session, string? system, string? code, string? targetSystem, string? conceptMap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return TerminologyCall.RunAsync(
            session,
            state => Answer(state.Store, TermArguments.Required(system, "system"), TermArguments.Required(code, "code"), TermArguments.Optional(targetSystem, "targetSystem"), TermArguments.Optional(conceptMap, "conceptMap")),
            cancellationToken);
    }

    internal static TerminologyAnswer<TermTranslateFacts> Answer(TerminologyStore store, string system, string code, string? targetSystem, string? conceptMap)
    {
        List<IssueItem> issues = [];
        List<ConceptMapEntry> candidates;
        if (conceptMap is not null)
        {
            CanonicalResolution<ConceptMapEntry> resolution = store.ResolveConceptMap(conceptMap);
            if (resolution.Entry is not { } named)
            {
                var notLoaded = new TermTranslateFacts(ConceptMapNotLoaded, system, code, targetSystem, conceptMap, 0, []);
                var missing = new AbsentItem($"ConceptMap {TerminologyNames.Canonical(resolution.Url, resolution.Version)}", store.Describe(), store.BuiltAt);
                return new TerminologyAnswer<TermTranslateFacts>(notLoaded, [], [], [missing], issues);
            }

            candidates = [named];
        }
        else
        {
            candidates = [.. store.ConceptMaps];
        }

        List<ConceptMapEntry> checkedMaps = [.. candidates.Where(map => Groups(map, system, targetSystem).Any())];
        List<TermTranslation> translations = [];
        foreach (ConceptMapEntry map in checkedMaps)
        {
            issues.AddRange(store.ConflictIssues("ConceptMap", map.Url, map.Version));
            foreach (ConceptMapGroup group in Groups(map, system, targetSystem))
            {
                translations.AddRange(Translate(map, group, code, issues));
            }
        }

        bool anyTarget = translations.Exists(translation => translation.Code is not null);
        string outcome = checkedMaps.Count == 0 ? NoMapForSystem : anyTarget ? Mapped : NotMapped;
        List<OmittedItem> omitted = [];
        (TermTranslateFacts facts, _) = TermText.FitPage(
            TermText.Take<TermTranslation>(translations, "translations", omitted),
            items => new TermTranslateFacts(outcome, system, code, targetSystem, conceptMap, checkedMaps.Count, items),
            "translations",
            omitted);

        List<AbsentItem> absent = outcome switch
        {
            NoMapForSystem => [new AbsentItem(
                $"a ConceptMap from {TerminologyNames.Canonical(system, null)}{(targetSystem is null ? string.Empty : $" to {TerminologyNames.Canonical(targetSystem, null)}")}",
                conceptMap is null ? $"the groups of every loaded ConceptMap ({store.Describe()})" : $"the groups of ConceptMap {TerminologyNames.Canonical(candidates[0].Url, candidates[0].Version)}",
                store.BuiltAt)],
            NotMapped => [new AbsentItem(
                $"a target for {TerminologyNames.Coded(system, code)}",
                $"{checkedMaps.Count} ConceptMaps with a group from that system: {string.Join(", ", checkedMaps.Take(5).Select(map => TerminologyNames.Canonical(map.Url, map.Version)))}{(checkedMaps.Count > 5 ? ", ..." : string.Empty)}",
                store.BuiltAt)],
            _ => [],
        };

        List<EvidenceItem> evidence = [.. checkedMaps.Select(map => map.Origin.ToEvidence()).Distinct().Take(20)];
        return new TerminologyAnswer<TermTranslateFacts>(facts, evidence, omitted, absent, issues);
    }

    private static IEnumerable<ConceptMapGroup> Groups(ConceptMapEntry map, string system, string? targetSystem) =>
        map.Groups.Where(group => group.Source == system && (targetSystem is null || group.Target == targetSystem));

    private static IEnumerable<TermTranslation> Translate(ConceptMapEntry map, ConceptMapGroup group, string code, List<IssueItem> issues)
    {
        AnswerSource source = AnswerSource.Local(map.Origin);
        IReadOnlyList<ConceptMapElement> elements = group.ElementsFor(code);
        if (elements.Count > 0)
        {
            foreach (ConceptMapElement element in elements)
            {
                if (element.NoMap || element.Targets.Count == 0)
                {
                    yield return new TermTranslation(map.Url, map.Version, source, group.Target, group.TargetVersion, null, null, null, null, null, false, "no-map");
                    continue;
                }

                foreach (ConceptMapTarget target in element.Targets)
                {
                    yield return new TermTranslation(
                        map.Url,
                        map.Version,
                        source,
                        group.Target,
                        group.TargetVersion,
                        target.Code,
                        target.Display is { } display ? display[..Math.Min(display.Length, TermText.MaxShortText)] : null,
                        target.Equivalence,
                        target.Relationship,
                        target.Comment is { } comment ? comment[..Math.Min(comment.Length, TermText.MaxShortText)] : null,
                        target.Conditional,
                        "element");
                }
            }

            yield break;
        }

        switch (group.Unmapped)
        {
            case { Mode: "provided" or "use-source-code" }:
                yield return new TermTranslation(map.Url, map.Version, source, group.Target, group.TargetVersion, code, null, "equal", null, null, false, "unmapped-provided");
                break;
            case { Mode: "fixed", Code: { } fixedCode } unmapped:
                yield return new TermTranslation(map.Url, map.Version, source, group.Target, group.TargetVersion, fixedCode, unmapped.Display, null, null, null, false, "unmapped-fixed");
                break;
            case { Mode: "other-map" } other:
                issues.Add(new IssueItem("information", "unmapped-other-map-not-followed", $"A group of ConceptMap {TerminologyNames.Canonical(map.Url, map.Version)} sends codes it does not list to another map{(other.OtherMap is { } url ? $" ({TerminologyNames.Canonical(url, null)})" : string.Empty)}, which this tool does not follow; translate with that map.", map.Origin.ToEvidence().File));
                break;
        }
    }
}
