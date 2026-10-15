// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// The folder's per-type indexes as the source of an in-memory search. A type's index is built (or loaded from the
/// cache) only when the search first reads that type, and a reference is resolved through the catalog first, so a
/// reference to a resource the folder does not hold never builds an index. Created for one search; not thread-safe.
/// </summary>
internal sealed class FolderSearchSource(FolderSession session) : IInMemorySearchSource
{
    private readonly Dictionary<string, (ResourceTypeIndex Index, Dictionary<string, IndexedResource> ById)> _types = new(StringComparer.Ordinal);

    private readonly List<ResourceTypeIndex> _read = [];

    /// <summary>The indexes this search read, in the order it first read them.</summary>
    public IReadOnlyList<ResourceTypeIndex> IndexesRead => _read;

    public IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Enumerate(string resourceType) =>
        Type(resourceType).Index.Resources.Select(resource => (resource.Key, resource.Index));

    public (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)? Resolve(string resourceType, string id)
    {
        if (!session.Schema.ResourceTypeNames.Contains(resourceType) || session.FindEntry(resourceType, id) is null)
        {
            return null;
        }

        return Type(resourceType).ById.TryGetValue(id, out IndexedResource? resource) ? (resource.Key, resource.Index) : null;
    }

    /// <summary>The indexed resources of <paramref name="resourceType"/>, in catalog order.</summary>
    public IReadOnlyList<IndexedResource> Resources(string resourceType) => Type(resourceType).Index.Resources;

    /// <summary>
    /// Every resource in compartment <paramref name="compartmentType"/>/<paramref name="compartmentId"/>, across the types that
    /// can be in it and that the folder holds, type by type in ordinal order and in catalog order within a type. Only those
    /// types' indexes are read. A type whose membership cannot be evaluated (see
    /// <see cref="InMemoryCompartmentMembership.UnevaluableParameters"/>) is not read and is returned in
    /// <c>UnevaluableTypes</c>, so a caller can report what the enumeration left out.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="compartmentType"/> is not a compartment type of the session's FHIR version.</exception>
    public (IReadOnlyList<IndexedResource> Members, IReadOnlyList<string> UnevaluableTypes) CompartmentMembers(string compartmentType, string compartmentId)
    {
        InMemoryCompartmentMembership membership = session.Compartments;
        List<IndexedResource> members = [];
        List<string> unevaluable = [];
        foreach (string type in membership.ResourceTypes(compartmentType).Where(session.HoldsType))
        {
            if (membership.UnevaluableParameters(compartmentType, type).Count > 0)
            {
                unevaluable.Add(type);
                continue;
            }

            members.AddRange(Resources(type).Where(resource => membership.IsInCompartment(resource.Key, resource.Index, compartmentType, compartmentId)));
        }

        return (members, unevaluable);
    }

    /// <summary>
    /// The members of each compartment <paramref name="compartmentType"/>/id for every id in <paramref name="compartmentIds"/>,
    /// in one pass over the types that can be in such a compartment: a resource is a candidate for the compartments its index
    /// references locally (and, for the compartment's own type, its own), and a member of each one
    /// <see cref="InMemoryCompartmentMembership.IsInCompartment"/> confirms. So each id gets exactly what
    /// <see cref="CompartmentMembers"/> returns for it, in the same order, at the cost of one pass instead of one per id.
    /// </summary>
    public (IReadOnlyDictionary<string, List<IndexedResource>> Members, IReadOnlyList<string> UnevaluableTypes) CompartmentMembersOf(string compartmentType, IReadOnlySet<string> compartmentIds)
    {
        ArgumentNullException.ThrowIfNull(compartmentIds);

        InMemoryCompartmentMembership membership = session.Compartments;
        Dictionary<string, List<IndexedResource>> members = compartmentIds.ToDictionary(id => id, _ => new List<IndexedResource>(), StringComparer.Ordinal);
        List<string> unevaluable = [];
        foreach (string type in membership.ResourceTypes(compartmentType).Where(session.HoldsType))
        {
            if (membership.UnevaluableParameters(compartmentType, type).Count > 0)
            {
                unevaluable.Add(type);
                continue;
            }

            foreach (IndexedResource resource in Resources(type))
            {
                foreach (string candidate in Candidates(resource, compartmentType))
                {
                    if (members.TryGetValue(candidate, out List<IndexedResource>? list) && membership.IsInCompartment(resource.Key, resource.Index, compartmentType, candidate))
                    {
                        list.Add(resource);
                    }
                }
            }
        }

        return (members, unevaluable);
    }

    /// <summary>The ids of the <paramref name="compartmentType"/> compartments <paramref name="resource"/> could be in, each once.</summary>
    private static IEnumerable<string> Candidates(IndexedResource resource, string compartmentType)
    {
        IEnumerable<string> referenced = resource.Index
            .Select(entry => entry.Value)
            .OfType<ReferenceSearchValue>()
            .Where(reference => reference.BaseUri is null && reference.ResourceType == compartmentType)
            .Select(reference => reference.ResourceId);
        return (resource.Key.ResourceType == compartmentType ? referenced.Prepend(resource.Key.Id) : referenced).Distinct(StringComparer.Ordinal);
    }

    /// <summary>The indexed resource behind a location this source returned.</summary>
    public IndexedResource Indexed(ResourceKey location) => Type(location.ResourceType).ById[location.Id];

    /// <summary>The type's index, built or loaded on first use, with its resources by id (the catalog holds one per type and id).</summary>
    private (ResourceTypeIndex Index, Dictionary<string, IndexedResource> ById) Type(string resourceType)
    {
        if (!_types.TryGetValue(resourceType, out (ResourceTypeIndex Index, Dictionary<string, IndexedResource> ById) type))
        {
            ResourceTypeIndex index = session.GetIndex(resourceType);
            type = (index, index.Resources.ToDictionary(resource => resource.Key.Id, StringComparer.Ordinal));
            _types.Add(resourceType, type);
            _read.Add(index);
        }

        return type;
    }
}
