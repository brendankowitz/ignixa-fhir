// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// The record kinds of each resource type in a folder's catalog, with their counts and the short name each is shown by. A kind
/// is a <see cref="RecordKind"/> (profiles, else an Encounter's class or an Observation's category), split by identifier system
/// when every resource of that kind has identifiers of one system and they are not all the same one: MIMIC-IV on FHIR gives hospital, ED and ICU
/// encounters one profile and tells them apart only by <c>identifier.system</c> (<c>encounter-hosp</c>, <c>encounter-ed</c>,
/// <c>encounter-icu</c>). A short name is unique within its type: two kinds that would share one are shown in full.
/// </summary>
public sealed class FolderKinds
{
    /// <summary>The short name of resources with no kind, in a type that has kinds.</summary>
    public const string None = "(none)";

    /// <summary>The <see cref="Tally"/> key of the kinds beyond its limit.</summary>
    public const string Other = "(other)";

    private static readonly KindCount NoKind = new(null, null, None, null, 0);

    private readonly Dictionary<string, IReadOnlyList<KindCount>> _byType;
    private readonly Dictionary<(string ResourceType, string? Kind, string? System), KindCount> _byKey;

    private FolderKinds(Dictionary<string, IReadOnlyList<KindCount>> byType, Dictionary<(string, string?, string?), KindCount> byKey)
    {
        _byType = byType;
        _byKey = byKey;
    }

    /// <summary>Counts the kinds of every catalogued resource.</summary>
    public static FolderKinds From(FolderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        // One pass over the catalog, by instance (the catalog shares one string per distinct type, kind and system), then by
        // value over the few distinct triples; everything after works on those.
        Dictionary<(string Type, string? Kind, string? System), int> byInstance = new(SameInstances.Comparer);
        foreach (CatalogEntry entry in catalog.Entries)
        {
            ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(byInstance, (entry.ResourceType, entry.Kind, entry.IdentifierSystem), out _);
            count++;
        }

        Dictionary<(string Type, string? Kind, string? System), int> tally = [];
        foreach (((string Type, string? Kind, string? System) key, int count) in byInstance)
        {
            tally[key] = tally.GetValueOrDefault(key) + count;
        }

        Dictionary<string, IReadOnlyList<KindCount>> byType = new(StringComparer.Ordinal);
        Dictionary<(string, string?, string?), KindCount> byKey = [];
        foreach (IGrouping<string, KeyValuePair<(string Type, string? Kind, string? System), int>> type in tally.GroupBy(pair => pair.Key.Type, StringComparer.Ordinal))
        {
            // A kind is split by identifier system only when every one of its resources has one system and they do not all share it.
            List<(string? Kind, string? System, int Count)> kinds = [];
            foreach (IGrouping<string?, KeyValuePair<(string Type, string? Kind, string? System), int>> kind in type.GroupBy(pair => pair.Key.Kind, StringComparer.Ordinal))
            {
                bool split = kind.Key is not null && kind.Count() > 1 && kind.All(pair => pair.Key.System is not null);
                kinds.AddRange(split
                    ? kind.Select(pair => (kind.Key, pair.Key.System, pair.Value))
                    : [(kind.Key, (string?)null, kind.Sum(pair => pair.Value))]);
            }

            // A split kind's identifier system alone keeps it when no other kind of the type has that system.
            Dictionary<string, int> kindsPerSystem = type
                .Where(pair => pair.Key.System is not null)
                .GroupBy(pair => pair.Key.System!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key.Kind).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
            IReadOnlyList<string> names = ShortNames([.. kinds.Select(kind => (kind.Kind, kind.System))]);
            List<KindCount> counts = [.. kinds
                .Select((kind, index) => new KindCount(
                    kind.Kind,
                    kind.System,
                    names[index],
                    kind.System is { } system && kindsPerSystem[system] == 1 ? RecordKind.Filter(null, system) : RecordKind.Filter(kind.Kind, kind.System),
                    kind.Count))
                .OrderByDescending(kind => kind.Count)
                .ThenBy(kind => kind.ShortName, StringComparer.Ordinal)];
            foreach (KindCount kind in counts)
            {
                byKey[(type.Key, kind.Kind, kind.IdentifierSystem)] = kind;
            }

            byType[type.Key] = counts;
        }

        return new FolderKinds(byType, byKey);
    }

    /// <summary>The kinds of <paramref name="resourceType"/>, most resources first; empty when the folder holds none of the type.</summary>
    public IReadOnlyList<KindCount> Of(string resourceType) => _byType.GetValueOrDefault(resourceType) ?? [];

    /// <summary>Whether the folder's <paramref name="resourceType"/> resources are of more than one kind (no kind counting as one).</summary>
    public bool IsMixed(string resourceType) => Of(resourceType).Count > 1;

    /// <summary>The types whose resources are of more than one kind, in ordinal order.</summary>
    public IEnumerable<string> MixedTypes => _byType.Keys.Where(IsMixed).Order(StringComparer.Ordinal);

    /// <summary>
    /// Each type of more than one kind, with its kinds' counts by short name (most first); null when every type is of one kind.
    /// The folder overview's breakdown.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? MixedCounts()
    {
        Dictionary<string, IReadOnlyDictionary<string, int>> mixed = MixedTypes.ToDictionary(
            type => type,
            IReadOnlyDictionary<string, int> (type) => Of(type).ToDictionary(kind => kind.ShortName, kind => kind.Count, StringComparer.Ordinal),
            StringComparer.Ordinal);
        return mixed.Count == 0 ? null : mixed;
    }

    /// <summary>The kind of <paramref name="entry"/> (its <see cref="KindCount.Count"/> is the folder's count of that kind).</summary>
    public KindCount KindOf(CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _byKey.GetValueOrDefault((entry.ResourceType, entry.Kind, entry.IdentifierSystem))
            ?? _byKey.GetValueOrDefault((entry.ResourceType, entry.Kind, null))
            ?? NoKind;
    }

    /// <summary>
    /// The profile canonical a <c>_profile</c> value names when it is not a canonical itself: the one profile of a
    /// <paramref name="resourceType"/> kind whose last segment it is, or the first profile of the one kind shown by that name;
    /// null when none or several are.
    /// </summary>
    public string? ProfileNamed(string resourceType, string shortName)
    {
        IEnumerable<string> bySegment = Of(resourceType)
            .Where(kind => kind.Kind is not null)
            .SelectMany(kind => RecordKind.Profiles(kind.Kind!))
            .Where(profile => string.Equals(RecordKind.ShortName(profile), shortName, StringComparison.Ordinal));
        IEnumerable<string> byKindName = Of(resourceType)
            .Where(kind => kind.Kind is not null && kind.IdentifierSystem is null && string.Equals(kind.ShortName, shortName, StringComparison.Ordinal))
            .SelectMany(kind => RecordKind.Profiles(kind.Kind!).Take(1));
        List<string> matches = [.. bySegment.Concat(byKindName).Distinct(StringComparer.Ordinal)];
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// The kinds of <paramref name="entries"/>, most first (then by short name): the first <paramref name="limit"/> with their
    /// counts, and the count of the rest.
    /// </summary>
    public (IReadOnlyList<(KindCount Kind, int Count)> Top, int Others) Count(IEnumerable<CatalogEntry> entries, int limit)
    {
        ArgumentNullException.ThrowIfNull(entries);

        List<(KindCount Kind, int Count)> counts = [.. entries
            .GroupBy(KindOf)
            .Select(group => (group.Key, group.Count()))
            .OrderByDescending(kind => kind.Item2)
            .ThenBy(kind => kind.Item1.ShortName, StringComparer.Ordinal)];
        return ([.. counts.Take(limit)], counts.Skip(limit).Sum(kind => kind.Count));
    }

    /// <summary>Counts by short name, most first, the ones beyond <paramref name="limit"/> summed under <see cref="Other"/>.</summary>
    public IReadOnlyDictionary<string, int> Tally(IEnumerable<CatalogEntry> entries, int limit)
    {
        (IReadOnlyList<(KindCount Kind, int Count)> top, int rest) = Count(entries, limit);
        Dictionary<string, int> tally = top.ToDictionary(kind => kind.Kind.ShortName, kind => kind.Count, StringComparer.Ordinal);
        if (rest > 0)
        {
            tally[Other] = rest;
        }

        return tally;
    }

    /// <summary>
    /// The names one type's kinds are shown by, in their order. A kind is named by its profiles' last segments (<see
    /// cref="RecordKind.ShortName"/>), with its identifier system's last segment in parentheses when it was split by one, or by
    /// that segment alone when every kind of the type was split from one profile. The words (between hyphens) every name starts
    /// with (of the names made of profiles or systems) are then dropped, keeping at least one: MIMIC's mimic-observation-chartevents and mimic-observation-labevents are
    /// chartevents and labevents, and its encounter-hosp, encounter-ed and encounter-icu are hosp, ed and icu. Names that would
    /// still be shared fall back to the kinds written in full.
    /// </summary>
    private static IReadOnlyList<string> ShortNames(IReadOnlyList<(string? Kind, string? System)> kinds)
    {
        bool bySystemAlone = kinds.Count > 1
            && kinds.All(kind => kind.System is not null)
            && kinds.Select(kind => kind.Kind).Distinct(StringComparer.Ordinal).Count() == 1;
        List<string> names = [.. kinds.Select(kind => Name(kind.Kind, kind.System, bySystemAlone))];

        // Only names made of profile or system segments are trimmed; (none) and class or category kinds keep their names.
        List<int> trimmable = [.. Enumerable.Range(0, kinds.Count).Where(index => bySystemAlone || (kinds[index].Kind is { } kind && RecordKind.Profiles(kind).Count > 0))];
        if (trimmable.Count > 1)
        {
            List<string[]> words = [.. trimmable.Select(index => names[index].Split('-'))];
            int shared = 0;
            while (shared < words.Min(word => word.Length) - 1 && words.All(word => string.Equals(word[shared], words[0][shared], StringComparison.Ordinal)))
            {
                shared++;
            }

            List<string> trimmed = [.. names];
            for (int position = 0; position < trimmable.Count; position++)
            {
                trimmed[trimmable[position]] = string.Join('-', words[position].Skip(shared));
            }

            if (trimmed.Distinct(StringComparer.Ordinal).Count() == trimmed.Count)
            {
                return trimmed;
            }
        }

        return names.Distinct(StringComparer.Ordinal).Count() == names.Count
            ? names
            : [.. kinds.Select(kind => kind.System is null ? kind.Kind ?? None : $"{kind.Kind ?? None} ({kind.System})")];
    }

    private static string Name(string? kind, string? system, bool bySystemAlone)
    {
        string? systemName = system is null ? null : RecordKind.ShortName(system);
        if (bySystemAlone)
        {
            return systemName!;
        }

        string kindName = kind is null ? None : RecordKind.ShortName(kind);
        return systemName is null ? kindName : $"{kindName} ({systemName})";
    }

    private sealed class SameInstances : IEqualityComparer<(string, string?, string?)>
    {
        public static readonly SameInstances Comparer = new();

        public bool Equals((string, string?, string?) x, (string, string?, string?) y) =>
            ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2) && ReferenceEquals(x.Item3, y.Item3);

        public int GetHashCode((string, string?, string?) value) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(value.Item1), value.Item2 is null ? 0 : RuntimeHelpers.GetHashCode(value.Item2), value.Item3 is null ? 0 : RuntimeHelpers.GetHashCode(value.Item3));
    }

    /// <summary>
    /// One kind of a type: the <see cref="RecordKind"/> (null for resources without one), the identifier system it was split by
    /// (null when it was not), its short name, the search parameters that keep it (null when nothing does), and its count.
    /// </summary>
    public sealed record KindCount(string? Kind, string? IdentifierSystem, string ShortName, string? Filter, int Count);
}
