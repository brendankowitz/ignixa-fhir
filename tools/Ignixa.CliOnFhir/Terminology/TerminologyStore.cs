// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// An immutable snapshot of every loaded terminology source, in load order. A url asked for without a version resolves by
/// <see cref="VersionPrecedence"/> (the folder; then package authority, with hl7.terminology.* first for terminology.hl7.org
/// canonicals and core last; then the highest orderable version within one package; then load order). Two sources
/// defining the same <c>url|version</c> with the same content count once; with different content the one of higher
/// precedence is used and a <see cref="CanonicalConflict"/> is recorded, which every answer that uses it reports.
/// Loading a package builds a new snapshot (<see cref="With"/>), so a call always answers from one consistent state.
/// </summary>
public sealed class TerminologyStore
{
    private readonly Dictionary<string, List<CodeSystemEntry>> _codeSystems;
    private readonly Dictionary<string, List<ValueSetEntry>> _valueSets;
    private readonly Dictionary<string, List<ConceptMapEntry>> _conceptMaps;
    private readonly Dictionary<(string Type, string Url, string? Version), List<CanonicalConflict>> _conflicts;

    private TerminologyStore(IReadOnlyList<LoadedSource> sources, DateTimeOffset builtAt)
    {
        Sources = sources;
        BuiltAt = builtAt;

        // Each url's entries are indexed by its precedence (authority rank, then load order, then file order), so a
        // conflict keeps the higher-precedence definition.
        List<CanonicalConflict> conflicts = [];
        _codeSystems = Index(sources.SelectMany(source => source.CodeSystems), "CodeSystem", entry => entry.Url, entry => entry.Version, entry => entry.Fingerprint, entry => entry.Origin, conflicts);
        _valueSets = Index(sources.SelectMany(source => source.ValueSets), "ValueSet", entry => entry.Url, entry => entry.Version, entry => entry.Fingerprint, entry => entry.Origin, conflicts);
        _conceptMaps = Index(sources.SelectMany(source => source.ConceptMaps), "ConceptMap", entry => entry.Url, entry => entry.Version, entry => entry.Fingerprint, entry => entry.Origin, conflicts);
        Conflicts = conflicts;
        _conflicts = conflicts.GroupBy(conflict => (conflict.ResourceType, conflict.Url, conflict.Version)).ToDictionary(group => group.Key, group => group.ToList());
    }

    public static TerminologyStore Empty { get; } = new([], DateTimeOffset.MinValue);

    /// <summary>The sources in load order: the folder, the packages loaded explicitly, the default core package, then later loads.</summary>
    public IReadOnlyList<LoadedSource> Sources { get; }

    /// <summary>When this snapshot was built.</summary>
    public DateTimeOffset BuiltAt { get; }

    public IReadOnlyList<CanonicalConflict> Conflicts { get; }

    /// <summary>ValueSet expansions computed over this snapshot, keyed by url and version; valid for its lifetime.</summary>
    internal ConcurrentDictionary<(string Url, string? Version), Lazy<ValueSetExpansion>> Expansions { get; } = new();

    /// <summary>Every CodeSystem entry kept (identical repeats dropped), in precedence then file order.</summary>
    public IEnumerable<CodeSystemEntry> CodeSystems => _codeSystems.Values.SelectMany(entries => entries).OrderBy(Rank);

    public IEnumerable<ValueSetEntry> ValueSets => _valueSets.Values.SelectMany(entries => entries).OrderBy(Rank);

    public IEnumerable<ConceptMapEntry> ConceptMaps => _conceptMaps.Values.SelectMany(entries => entries).OrderBy(Rank);

    public static TerminologyStore Build(IReadOnlyList<LoadedSource> sources, DateTimeOffset builtAt)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return new TerminologyStore(sources, builtAt);
    }

    /// <summary>A new snapshot with <paramref name="source"/> after the existing ones (lowest precedence).</summary>
    public TerminologyStore With(LoadedSource source, DateTimeOffset builtAt)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new TerminologyStore([.. Sources, source], builtAt);
    }

    /// <summary>
    /// The CodeSystem version that answers for <paramref name="url"/>: exactly <paramref name="version"/>, or without one the
    /// candidate <see cref="VersionPrecedence"/> chooses, with an <c>other-versions-loaded</c> note when there were several.
    /// </summary>
    public CodeSystemResolution ResolveCodeSystem(string url, string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        List<CodeSystemEntry> entries = _codeSystems.GetValueOrDefault(url) ?? [];
        List<CodeSystemEntry> providing = [.. entries.Where(entry => entry.ProvidesConcepts)];
        CodeSystemEntry? entry = version is null
            ? VersionPrecedence.Choose(providing, url, candidate => candidate.Version, candidate => candidate.VersionAlgorithm, candidate => candidate.Origin.Source, LoadOrderOf)
            : providing.FirstOrDefault(candidate => string.Equals(candidate.Version, version, StringComparison.Ordinal));
        IssueItem? others = version is null && entry is not null && providing.Count > 1
            ? OtherVersions("CodeSystem", url, entry.Version, entry.Origin, providing.Select(candidate => (candidate.Version, candidate.Origin)))
            : null;
        return new CodeSystemResolution(url, version, entry, entry is null ? providing : [], [.. entries.Where(candidate => !candidate.ProvidesConcepts)], others);
    }

    public CanonicalResolution<ValueSetEntry> ResolveValueSet(string canonical) =>
        Resolve(_valueSets, "ValueSet", canonical, entry => entry.Version, entry => entry.VersionAlgorithm, entry => entry.Origin, LoadOrderOf);

    public CanonicalResolution<ConceptMapEntry> ResolveConceptMap(string canonical) =>
        Resolve(_conceptMaps, "ConceptMap", canonical, entry => entry.Version, entry => entry.VersionAlgorithm, entry => entry.Origin, LoadOrderOf);

    /// <summary>The <c>canonical-conflict</c> warnings for one url and version.</summary>
    public IEnumerable<IssueItem> ConflictIssues(string resourceType, string url, string? version) =>
        _conflicts.GetValueOrDefault((resourceType, url, version))?.Select(conflict => conflict.ToIssue()) ?? [];

    /// <summary>
    /// The precedence of a source for <paramref name="url"/>, lower first: by authority rank (<see cref="VersionPrecedence.Rank"/>),
    /// then load order.
    /// </summary>
    public int PrecedenceOf(TerminologySourceInfo source, string url)
    {
        ArgumentNullException.ThrowIfNull(source);

        int loadOrder = LoadOrderOf(source);
        return loadOrder == int.MaxValue ? int.MaxValue : (VersionPrecedence.Rank(source, url) * Sources.Count) + loadOrder;
    }

    /// <summary>What a "not found" checked: the counts of loaded resources and the sources, by name.</summary>
    public string Describe() => Sources.Count == 0
        ? "no terminology sources are loaded"
        : $"{_codeSystems.Values.Sum(entries => entries.Count)} CodeSystems, {_valueSets.Values.Sum(entries => entries.Count)} ValueSets and {_conceptMaps.Values.Sum(entries => entries.Count)} ConceptMaps loaded from {string.Join(", ", Sources.Select(source => source.Info.Name))}";

    /// <summary>Splits <c>url|version</c>; the version is null when there is no <c>|</c>.</summary>
    public static (string Url, string? Version) SplitCanonical(string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        int bar = canonical.LastIndexOf('|');
        return bar > 0 ? (canonical[..bar], canonical[(bar + 1)..] is { Length: > 0 } version ? version : null) : (canonical, null);
    }

    private static CanonicalResolution<T> Resolve<T>(Dictionary<string, List<T>> index, string resourceType, string canonical, Func<T, string?> version, Func<T, string?> algorithm, Func<T, ResourceOrigin> origin, Func<TerminologySourceInfo, int> loadOrder)
        where T : class
    {
        (string url, string? requested) = SplitCanonical(canonical);
        List<T> entries = index.GetValueOrDefault(url) ?? [];
        T? entry = requested is null
            ? VersionPrecedence.Choose(entries, url, version, algorithm, candidate => origin(candidate).Source, loadOrder)
            : entries.FirstOrDefault(candidate => string.Equals(version(candidate), requested, StringComparison.Ordinal));
        IssueItem? others = requested is null && entry is not null && entries.Count > 1
            ? OtherVersions(resourceType, url, version(entry), origin(entry), entries.Select(candidate => (version(candidate), origin(candidate))))
            : null;
        return new CanonicalResolution<T>(url, requested, entry, [.. entries.Select(version)], others);
    }

    private static IssueItem OtherVersions(string resourceType, string url, string? chosen, ResourceOrigin chosenOrigin, IEnumerable<(string? Version, ResourceOrigin Origin)> candidates) => new(
        "information",
        CodeSystemResolution.OtherVersionsCode,
        $"{resourceType} {TerminologyNames.Canonical(url, null)} is loaded in several versions ({string.Join(", ", candidates.Select(candidate => $"{(candidate.Version is null ? "(no version)" : TerminologyNames.Version(candidate.Version))} from {candidate.Origin.Source.Name}"))}); "
            + $"this answer uses {(chosen is null ? "(no version)" : TerminologyNames.Version(chosen))} from {chosenOrigin.Source.Name} ({VersionPrecedence.RuleText}). Pass a version to choose another.",
        chosenOrigin.ToEvidence().File);

    private Dictionary<string, List<T>> Index<T>(
        IEnumerable<T> entries,
        string resourceType,
        Func<T, string> url,
        Func<T, string?> version,
        Func<T, string> fingerprint,
        Func<T, ResourceOrigin> origin,
        List<CanonicalConflict> conflicts)
    {
        var index = new Dictionary<string, List<T>>(StringComparer.Ordinal);

        // OrderBy is stable, so file order within one source is kept.
        foreach (T entry in entries.OrderBy(entry => PrecedenceOf(origin(entry).Source, url(entry))))
        {
            List<T> sameUrl = index.TryGetValue(url(entry), out List<T>? existing) ? existing : index[url(entry)] = [];
            T? sameVersion = sameUrl.FirstOrDefault(candidate => string.Equals(version(candidate), version(entry), StringComparison.Ordinal));
            if (sameVersion is null)
            {
                sameUrl.Add(entry);
            }
            else if (!string.Equals(fingerprint(sameVersion), fingerprint(entry), StringComparison.Ordinal))
            {
                conflicts.Add(new CanonicalConflict(resourceType, url(entry), version(entry), origin(sameVersion), origin(entry)));
            }
        }

        return index;
    }

    private int Rank(CodeSystemEntry entry) => PrecedenceOf(entry.Origin.Source, entry.Url);

    private int Rank(ValueSetEntry entry) => PrecedenceOf(entry.Origin.Source, entry.Url);

    private int Rank(ConceptMapEntry entry) => PrecedenceOf(entry.Origin.Source, entry.Url);

    private int LoadOrderOf(TerminologySourceInfo source)
    {
        for (int index = 0; index < Sources.Count; index++)
        {
            if (ReferenceEquals(Sources[index].Info, source))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
