// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure.Snapshot;

/// <summary>
/// Production <see cref="ISnapshotBaseResolver"/> for the profile-layering pipeline. Resolves a
/// <c>baseDefinition</c> canonical URL in two tiers:
/// <list type="number">
/// <item>Package profiles: indexed by canonical URL (version-stripped) from the loaded IG
/// resources — covers profile-on-profile bases and IG-supplied base StructureDefinitions.</item>
/// <item>Core types: projected on demand from the base <see cref="IFhirSchemaProvider"/>'s
/// pre-built <see cref="ITypeExtended"/> tree via <see cref="TypeSnapshotProjector"/>, since core
/// FHIR StructureDefinitions are not available as raw JSON in-process.</item>
/// </list>
/// Resolved nodes are cached and returned read-only; the generator deep-clones what it consumes.
/// </summary>
public sealed class PackageSnapshotBaseResolver : ISnapshotBaseResolver
{
    private readonly Dictionary<string, JsonObject> _packageByCanonical;
    private readonly Dictionary<string, JsonObject?> _coreCache = new(StringComparer.Ordinal);
    private readonly IFhirSchemaProvider _baseProvider;

    /// <summary>Initializes a new instance of the <see cref="PackageSnapshotBaseResolver"/> class.</summary>
    /// <param name="packageResources">Conformance resources extracted from the loaded IG packages.</param>
    /// <param name="baseProvider">Base FHIR-version schema provider used for core-type projection.</param>
    public PackageSnapshotBaseResolver(IEnumerable<ExtractedResource> packageResources, IFhirSchemaProvider baseProvider)
    {
        ArgumentNullException.ThrowIfNull(packageResources);
        ArgumentNullException.ThrowIfNull(baseProvider);

        _baseProvider = baseProvider;
        _packageByCanonical = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (var resource in packageResources)
        {
            if (resource.ResourceType != "StructureDefinition" || string.IsNullOrEmpty(resource.Canonical))
            {
                continue;
            }

            if (TryParse(resource.ResourceJson) is JsonObject structureDefinition)
            {
                _packageByCanonical[StripVersion(resource.Canonical)] = structureDefinition;
            }
        }
    }

    /// <inheritdoc/>
    public JsonObject? ResolveStructureDefinition(string canonicalUrl)
    {
        ArgumentException.ThrowIfNullOrEmpty(canonicalUrl);

        var url = StripVersion(canonicalUrl);
        if (_packageByCanonical.TryGetValue(url, out var packageDefinition))
        {
            return packageDefinition;
        }

        if (_coreCache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        var synthesized = ProjectCoreType(url);
        _coreCache[url] = synthesized;
        return synthesized;
    }

    private JsonObject? ProjectCoreType(string url)
    {
        var typeName = LastSegment(url);
        if (_baseProvider.GetTypeDefinition(typeName) is not ITypeExtended coreType)
        {
            return null;
        }

        return new JsonObject
        {
            ["resourceType"] = "StructureDefinition",
            ["url"] = url,
            ["type"] = typeName,
            ["snapshot"] = new JsonObject
            {
                ["element"] = TypeSnapshotProjector.Project(coreType),
            },
        };
    }

    private static JsonObject? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string StripVersion(string canonicalUrl)
    {
        var pipe = canonicalUrl.IndexOf('|', StringComparison.Ordinal);
        return pipe < 0 ? canonicalUrl : canonicalUrl[..pipe];
    }

    private static string LastSegment(string url)
    {
        var slash = url.LastIndexOf('/');
        return slash < 0 ? url : url[(slash + 1)..];
    }
}
