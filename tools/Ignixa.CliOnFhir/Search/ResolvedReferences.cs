// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Reading;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// <c>facts.resolvedReferences</c>: for each returned resource, the Bundle references it shows as written (<c>urn:uuid:...</c>,
/// an absolute or a relative URL) and the <c>Type/id</c> the index matched them as. Keyed by resource first because a
/// fullUrl is Bundle-scoped: one <c>urn:uuid</c> may name different resources in two Bundles. Returned resources keep their
/// original JSON, so this is what links them to the references searches, chains and includes used.
/// </summary>
internal static class ResolvedReferences
{
    /// <summary>Most references listed per response; the rest are counted in <c>omitted</c>.</summary>
    public const int Limit = 100;

    /// <summary>
    /// The first <paramref name="limit"/> resolutions of <paramref name="resources"/>, in order: what the response may list, so
    /// its size can be reserved before the resources are read.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> Candidates(IEnumerable<IndexedResource> resources, int limit) =>
        Candidates(resources.Select(resource => (ResourceReader.Describe(resource.Entry), resource.BundleReferences.Resolved)), limit);

    /// <summary>
    /// <see cref="Candidates(IEnumerable{IndexedResource}, int)"/> over each resource's <c>Type/id</c> and resolutions, for a
    /// caller that resolved them without the index (<c>read_resources</c>).
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> Candidates(IEnumerable<(string Reference, IReadOnlyDictionary<string, string> Resolved)> resources, int limit)
    {
        Dictionary<string, Dictionary<string, string>> candidates = new(StringComparer.Ordinal);
        int count = 0;
        foreach ((string resource, IReadOnlyDictionary<string, string> resolved) in resources)
        {
            foreach ((string reference, string target) in resolved.Take(limit - count))
            {
                Add(candidates, resource, reference, target);
                count++;
            }
        }

        return candidates;
    }

    /// <summary>
    /// The resolutions of the resources <paramref name="batch"/> returned that their returned JSON shows (an <c>elements</c>
    /// projection can drop a reference): those among <paramref name="candidates"/>, and how many more there were.
    /// </summary>
    public static (Dictionary<string, Dictionary<string, string>> Shown, int NotListed) Shown(
        IEnumerable<IndexedResource> requested,
        ResourceBatch batch,
        Dictionary<string, Dictionary<string, string>> candidates) =>
        Shown(requested.ToDictionary(resource => ResourceReader.Describe(resource.Entry), resource => resource.BundleReferences.Resolved, StringComparer.Ordinal), batch, candidates);

    /// <summary><see cref="Shown(IEnumerable{IndexedResource}, ResourceBatch, Dictionary{string, Dictionary{string, string}})"/> over each resource's resolutions by <c>Type/id</c>.</summary>
    public static (Dictionary<string, Dictionary<string, string>> Shown, int NotListed) Shown(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resolvedByResource,
        ResourceBatch batch,
        Dictionary<string, Dictionary<string, string>> candidates)
    {
        ArgumentNullException.ThrowIfNull(resolvedByResource);
        ArgumentNullException.ThrowIfNull(batch);

        Dictionary<string, Dictionary<string, string>> shown = new(StringComparer.Ordinal);
        int notListed = 0;
        foreach ((JsonNode returned, string reference) in batch.Resources.Zip(batch.Evidence.Select(item => item.Reference)))
        {
            IReadOnlyDictionary<string, string> resolved = resolvedByResource[reference];
            foreach (string written in BundleReferenceResolver.References(returned).Where(resolved.ContainsKey).Distinct(StringComparer.Ordinal))
            {
                if (candidates.GetValueOrDefault(reference)?.ContainsKey(written) == true)
                {
                    Add(shown, reference, written, resolved[written]);
                }
                else
                {
                    notListed++;
                }
            }
        }

        return (shown, notListed);
    }

    /// <summary>Both maps in one, <paramref name="first"/>'s resources first; a resource in both keeps every reference.</summary>
    public static Dictionary<string, Dictionary<string, string>> Merge(Dictionary<string, Dictionary<string, string>> first, Dictionary<string, Dictionary<string, string>> second)
    {
        Dictionary<string, Dictionary<string, string>> merged = new(StringComparer.Ordinal);
        foreach ((string resource, Dictionary<string, string> references) in first.Concat(second))
        {
            foreach ((string reference, string target) in references)
            {
                Add(merged, resource, reference, target);
            }
        }

        return merged;
    }

    /// <summary>
    /// The facts form of a resolved-references map: null when it lists nothing, so the member is left out. Resources and
    /// targets are named as every tool names them (<see cref="FhirIds"/>), and each reference as written by
    /// <see cref="FhirIds.Written"/>, so no prose id or reference text reaches facts outside the returned resources.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ToFacts(Dictionary<string, Dictionary<string, string>> resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        return resolved.Count == 0 ? null : resolved.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)pair.Value
                .GroupBy(reference => FhirIds.Written(reference.Key), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => FhirIds.Target(group.First().Value), StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    /// <summary>How many references <paramref name="map"/> lists.</summary>
    public static int Count(Dictionary<string, Dictionary<string, string>> map) => map.Values.Sum(references => references.Count);

    private static void Add(Dictionary<string, Dictionary<string, string>> map, string resource, string reference, string target)
    {
        if (!map.TryGetValue(resource, out Dictionary<string, string>? references))
        {
            references = new Dictionary<string, string>(StringComparer.Ordinal);
            map.Add(resource, references);
        }

        references.TryAdd(reference, target);
    }
}
