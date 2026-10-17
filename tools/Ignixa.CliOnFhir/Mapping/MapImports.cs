// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>
/// Resolves a map's <c>imports</c>, and theirs, from the maps the call gave (<c>--import</c>) and the folder's StructureMaps, and
/// merges every imported group into one map, so that a rule can call a group of an imported map and a group can extend one.
/// <c>Ignixa.FhirMappingLanguage</c>'s own import resolver is internal, and its loaders read files and the network; this resolver
/// reads neither. An <c>imports</c> url ending in <c>*</c> names every map whose url starts with the rest. A group whose name is
/// already taken (by the map itself or an earlier import) is not merged; an import nothing provides is an error that names it.
/// </summary>
internal static class MapImports
{
    /// <summary>The most maps one run reads, the map itself included.</summary>
    public const int MaxMaps = 32;

    /// <summary>The map with its imports' groups, and what was imported.</summary>
    public sealed record Resolved(MapExpression Map, IReadOnlyList<MapImportFacts> Imports, IReadOnlyList<IssueItem> Notes);

    /// <exception cref="ToolRequestException"><c>map-import-unresolved</c>, <c>map-import-limit</c>, or any error of a map given or found.</exception>
    public static Resolved Resolve(FolderSession session, LoadedMap loaded, MapExpression map, IReadOnlyList<string>? imports, Lazy<IReadOnlyList<FolderStructureMap>> folderMaps)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(folderMaps);

        FhirVersion version = session.Schema.Version;
        List<(LoadedMap Loaded, MapExpression Map)> given = [.. (imports ?? []).Select(import => MapLocator.Resolve(session, import, folderMaps)).Select(found => (found, MapCompiler.Compile(found, version)))];

        HashSet<string> included = new(StringComparer.Ordinal) { map.Url };
        List<(LoadedMap Loaded, MapExpression Map)> merged = [];
        Queue<(string Url, LoadedMap Importer)> pending = new(map.Imports.Select(import => (import.Url, loaded)));
        while (pending.TryDequeue(out (string Url, LoadedMap Importer) import))
        {
            List<(LoadedMap Loaded, MapExpression Map)> found = Find(import.Url, given, folderMaps, version);
            if (found.Count == 0)
            {
                string named = import.Importer.FromFolder ? FhirIds.Digest(import.Url) : QueryEcho.Of(import.Url);
                throw ToolRequestException.Error("map-import-unresolved", $"The map imports '{named}', which no map given with --import and no StructureMap of the folder provides; a map is never fetched from the network or a package.");
            }

            foreach ((LoadedMap source, MapExpression imported) in found.Where(candidate => included.Add(candidate.Map.Url)))
            {
                if (included.Count > MaxMaps)
                {
                    throw ToolRequestException.Error("map-import-limit", $"The map and its imports come to more than {MaxMaps} maps.");
                }

                merged.Add((source, imported));
                foreach (ImportsExpression next in imported.Imports)
                {
                    pending.Enqueue((next.Url, source));
                }
            }
        }

        return Merge(map, merged);
    }

    private static List<(LoadedMap Loaded, MapExpression Map)> Find(string url, List<(LoadedMap Loaded, MapExpression Map)> given, Lazy<IReadOnlyList<FolderStructureMap>> folderMaps, FhirVersion version)
    {
        List<(LoadedMap Loaded, MapExpression Map)> found = [.. given.Where(candidate => MapLocator.UrlMatches(url, candidate.Map.Url))];
        if (found.Count > 0)
        {
            return found;
        }

        return [.. MapLocator.ByUrl(folderMaps.Value, url).Select(candidate => (candidate.Loaded, MapCompiler.Compile(candidate.Loaded, version)))];
    }

    private static Resolved Merge(MapExpression map, List<(LoadedMap Loaded, MapExpression Map)> imported)
    {
        HashSet<string> names = new(map.Groups.Select(group => group.Name), StringComparer.OrdinalIgnoreCase);
        List<GroupExpression> groups = [.. map.Groups];
        int shadowed = 0;
        foreach (GroupExpression group in imported.SelectMany(item => item.Map.Groups))
        {
            if (names.Add(group.Name))
            {
                groups.Add(group);
            }
            else
            {
                shadowed++;
            }
        }

        List<IssueItem> notes = shadowed == 0
            ? []
            : [new IssueItem("information", "map-group-shadowed", $"{shadowed} group(s) of the imported maps have the name of a group the map or an earlier import already has, so the earlier one is used.", Location: null)];
        var merged = new MapExpression(
            map.Url,
            map.Identifier,
            map.Uses,
            map.Imports,
            groups,
            [.. map.ConceptMaps, .. imported.SelectMany(item => item.Map.ConceptMaps)],
            [.. map.Constants, .. imported.SelectMany(item => item.Map.Constants)],
            map.Metadata,
            map.Location,
            map.MetadataDeclarations);
        List<MapImportFacts> facts = [.. imported.Select(item => new MapImportFacts(FhirIds.Written(item.Map.Url), item.Loaded.Source, item.Loaded.Reference))];
        return new Resolved(merged, facts, notes);
    }
}
