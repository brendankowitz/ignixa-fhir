using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;

namespace Ignixa.Application.BackgroundOperations.Terminology;

/// <summary>
/// Snapshots import dependencies before job creation, keeping repository I/O and resource parsing out of replay.
/// </summary>
public static class TerminologyImportPlanner
{
    public static IReadOnlyList<TerminologyImportDependency> Create(IReadOnlyList<PackageResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var parsed = resources.OrderBy(resource => resource.PackageResourceId).Select(resource =>
            (Resource: resource, Json: JsonNode.Parse(resource.ResourceJson) as JsonObject
                ?? throw new InvalidOperationException($"PackageResource {resource.PackageResourceId} is not a JSON object."))).ToArray();
        if (parsed.Select(item => item.Resource.PackageResourceId).Distinct().Count() != parsed.Length)
        {
            throw new InvalidOperationException("Terminology dependency plans require distinct package resource IDs.");
        }
        var byCanonical = parsed.ToLookup(item =>
            (item.Resource.ResourceType, Canonical: item.Json["url"]?.GetValue<string>() ?? item.Resource.Canonical));
        var plan = new List<TerminologyImportDependency>(parsed.Length);
        foreach (var (resource, json) in parsed)
        {
            var dependencies = new SortedSet<long>();
            if (resource.ResourceType == "ValueSet" && json["expansion"] is not JsonObject && json["compose"] is JsonObject compose)
            {
                foreach (var (clause, isExclude) in ObjectsOf(compose["include"]).Select(clause => (clause, false))
                    .Concat(ObjectsOf(compose["exclude"]).Select(clause => (clause, true))))
                {
                    var concepts = clause["concept"] as JsonArray;
                    var valueSets = clause["valueSet"] as JsonArray;
                    var filters = clause["filter"] as JsonArray;
                    foreach (var canonical in valueSets?.Select(node => node?.GetValue<string>()).OfType<string>() ?? [])
                    {
                        var reference = TerminologyCanonicalReference.Parse(canonical);
                        foreach (var dependency in byCanonical[("ValueSet", reference.Url)])
                        {
                            if (reference.Version is null
                                || string.Equals(reference.Version, dependency.Json["version"]?.GetValue<string>(), StringComparison.Ordinal))
                            {
                                dependencies.Add(dependency.Resource.PackageResourceId);
                            }
                        }
                    }

                    // Filters and whole-system includes read CodeSystem concepts; whole-system excludes
                    // only remove a system's codes. A precomputed expansion bypasses compose.
                    if (clause["system"]?.GetValue<string>() is { } system
                        && (filters is { Count: > 0 } || (!isExclude && concepts is null or { Count: 0 } && valueSets is null or { Count: 0 })))
                    {
                        var version = clause["version"]?.GetValue<string>();
                        foreach (var dependency in byCanonical[("CodeSystem", system)])
                        {
                            if (version is null or "*" || string.Equals(version, dependency.Json["version"]?.GetValue<string>(), StringComparison.Ordinal))
                            {
                                dependencies.Add(dependency.Resource.PackageResourceId);
                            }
                        }
                    }
                }
            }
            plan.Add(new TerminologyImportDependency(resource.PackageResourceId, resource.Canonical, resource.ResourceType, [.. dependencies]));
        }
        return plan;
    }

    private static IEnumerable<JsonObject> ObjectsOf(JsonNode? node)
        => node is JsonArray array ? array.OfType<JsonObject>() : [];
}
