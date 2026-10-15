// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
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
