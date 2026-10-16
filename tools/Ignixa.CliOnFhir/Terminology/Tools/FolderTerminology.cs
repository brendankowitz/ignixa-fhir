// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// Which codes the folder uses and how they resolve: computed from the token entries of every resource type's search index
/// (building or loading those indexes), so it answers for exactly the codes searches see.
/// </summary>
public static class FolderTerminology
{
    public const string Rule = "Distinct system|code pairs of the folder's token search-index entries over every resource type, "
        + "leaving out parameters over Identifier elements (code 'identifier', or an expression naming an identifier element); "
        + "each code is resolved against the version of its system the store answers with by default (the first loaded source that provides concepts).";

    public static (TermFolderUsage Usage, OmittedItem? Omitted) Compute(FolderSession session, TerminologyStore store, int limit)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(store);

        var codes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string type in session.Catalog.Entries.Select(entry => entry.ResourceType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            foreach (SearchIndexEntry entry in session.GetIndex(type).Resources.SelectMany(resource => resource.Index))
            {
                if (entry.Value is TokenSearchValue { System: { Length: > 0 } system, Code: { Length: > 0 } code } && !OverIdentifier(entry.SearchParameter))
                {
                    (codes.TryGetValue(system, out HashSet<string>? set) ? set : codes[system] = new HashSet<string>(StringComparer.Ordinal)).Add(code);
                }
            }
        }

        List<SystemUsage> systems = [.. codes
            .Select(pair => Resolve(store, pair.Key, pair.Value))
            .OrderByDescending(usage => usage.DistinctCodes)
            .ThenBy(usage => usage.System, StringComparer.Ordinal)];
        OmittedItem? omitted = systems.Count > limit ? new OmittedItem($"{systems.Count - limit} more systems used in the folder", "limit") : null;
        return (new TermFolderUsage(TermFolderUsage.LoadedStatus, Rule, systems.Count, [.. systems.Take(limit)]), omitted);
    }

    /// <summary>
    /// Per distinct code of <paramref name="system"/> in the token search-index entries of <paramref name="types"/> (by the
    /// same rule as <see cref="Compute"/>), how many resources carry it. Types the folder does not hold are skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ResourcesPerCode(FolderSession session, string system, IEnumerable<string> types)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(system);
        ArgumentNullException.ThrowIfNull(types);

        HashSet<string> held = [.. session.Catalog.Entries.Select(entry => entry.ResourceType)];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string type in types.Where(held.Contains))
        {
            foreach (IndexedResource resource in session.GetIndex(type).Resources)
            {
                IEnumerable<string> codes = Codings(resource)
                    .Where(token => token.System == system)
                    .Select(token => token.Code)
                    .Distinct(StringComparer.Ordinal);
                foreach (string code in codes)
                {
                    counts[code] = counts.GetValueOrDefault(code) + 1;
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// Which of <paramref name="wanted"/> (system to codes) the folder uses, by the same rule as <see cref="Compute"/>, over every
    /// resource type it holds: per code the resources carrying it, and per resource type and search parameter the resources
    /// carrying any of them, so a caller can name the search that finds them.
    /// </summary>
    public static FolderCodeUses Uses(FolderSession session, IReadOnlyDictionary<string, HashSet<string>> wanted)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(wanted);

        var perCode = new Dictionary<(string System, string Code), int>();
        var perSearch = new Dictionary<(string ResourceType, string Parameter), int>();
        foreach (string type in session.Catalog.Entries.Select(entry => entry.ResourceType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            foreach (IndexedResource resource in session.GetIndex(type).Resources)
            {
                List<(string Parameter, string System, string Code)> hits = [.. Codings(resource).Where(token => wanted.TryGetValue(token.System, out HashSet<string>? codes) && codes.Contains(token.Code))];
                foreach ((string system, string code) in hits.Select(hit => (hit.System, hit.Code)).Distinct())
                {
                    perCode[(system, code)] = perCode.GetValueOrDefault((system, code)) + 1;
                }

                foreach (string parameter in hits.Select(hit => hit.Parameter).Distinct(StringComparer.Ordinal))
                {
                    perSearch[(type, parameter)] = perSearch.GetValueOrDefault((type, parameter)) + 1;
                }
            }
        }

        return new FolderCodeUses(
            [.. perCode.Select(pair => new FolderCodeUse(pair.Key.System, pair.Key.Code, pair.Value))],
            [.. perSearch
                .Select(pair => new FolderCodeSearch(pair.Key.ResourceType, pair.Key.Parameter, pair.Value))
                .OrderByDescending(search => search.Resources)
                .ThenBy(search => search.Parameter == "code" ? 0 : 1)
                .ThenBy(search => search.Parameter, StringComparer.Ordinal)
                .ThenBy(search => search.ResourceType, StringComparer.Ordinal)]);
    }

    /// <summary>The coded token entries of a resource's index, leaving out parameters over Identifier elements.</summary>
    private static IEnumerable<(string Parameter, string System, string Code)> Codings(IndexedResource resource) =>
        resource.Index
            .Where(entry => !OverIdentifier(entry.SearchParameter))
            .Select(entry => entry.Value is TokenSearchValue { System: { Length: > 0 } system, Code: { Length: > 0 } code } ? (entry.SearchParameter.Code, system, code) : default)
            .Where(token => token.code is not null);

    private static bool OverIdentifier(SearchParameterInfo parameter) =>
        parameter.Code == "identifier" || (parameter.Expression?.Contains("identifier", StringComparison.OrdinalIgnoreCase) ?? false);

    private static SystemUsage Resolve(TerminologyStore store, string system, HashSet<string> codes)
    {
        if (store.ResolveCodeSystem(system, version: null).Entry is not { } entry)
        {
            return new SystemUsage(system, CodeSystemResolution.NotLoaded, null, null, null, codes.Count, null, null, null);
        }

        int resolved = 0;
        int inactive = 0;
        foreach (string code in codes)
        {
            if (entry.Find(code) is { } concept)
            {
                resolved++;
                inactive += concept.Inactive ? 1 : 0;
            }
        }

        return new SystemUsage(system, TermFolderUsage.LoadedStatus, entry.Version, entry.Origin.Source.Name, entry.IsComplete, codes.Count, resolved, inactive, codes.Count - resolved);
    }
}
