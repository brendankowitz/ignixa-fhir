// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// Enumerates the string-valued leaves of a resource's JSON tree as <see cref="MutationTarget"/>s,
/// building dotted/indexed JSON paths and skipping FHIR infrastructure keys.
/// </summary>
/// <remarks>
/// This MVP only mutates strings, so only string-valued leaves are yielded. Numbers, booleans and
/// nulls are ignored. Infrastructure keys (resourceType, id, meta, implicitRules, text) are not
/// descended into.
/// </remarks>
public static class ResourceTreeWalker
{
    private static readonly FrozenSet<string> SkippedKeys = new[]
    {
        "resourceType", "id", "meta", "implicitRules", "text",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Walks the resource and returns every mutable string-valued leaf.</summary>
    public static IReadOnlyList<MutationTarget> Walk(ResourceJsonNode resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var targets = new List<MutationTarget>();
        WalkObject(resource.MutableNode, string.Empty, targets);
        return targets;
    }

    private static void WalkObject(JsonObject obj, string pathPrefix, List<MutationTarget> targets)
    {
        foreach (var property in obj)
        {
            if (SkippedKeys.Contains(property.Key))
            {
                continue;
            }

            var childPath = pathPrefix.Length == 0 ? property.Key : $"{pathPrefix}.{property.Key}";
            WalkProperty(obj, property.Key, property.Value, childPath, targets);
        }
    }

    private static void WalkProperty(JsonObject parent, string key, JsonNode? value, string path, List<MutationTarget> targets)
    {
        switch (value)
        {
            case JsonObject childObject:
                WalkObject(childObject, path, targets);
                break;
            case JsonArray childArray:
                WalkArray(childArray, key, path, targets);
                break;
            case JsonValue jsonValue when TryGetString(jsonValue, out var str):
                targets.Add(MutationTarget.ForProperty(parent, key, path, str));
                break;
            default:
                break;
        }
    }

    private static void WalkArray(JsonArray array, string elementName, string path, List<MutationTarget> targets)
    {
        for (var i = 0; i < array.Count; i++)
        {
            var itemPath = $"{path}[{i}]";
            WalkArrayItem(array, i, elementName, itemPath, targets);
        }
    }

    private static void WalkArrayItem(JsonArray array, int index, string elementName, string path, List<MutationTarget> targets)
    {
        switch (array[index])
        {
            case JsonObject childObject:
                WalkObject(childObject, path, targets);
                break;
            case JsonArray childArray:
                WalkArray(childArray, elementName, path, targets);
                break;
            case JsonValue jsonValue when TryGetString(jsonValue, out var str):
                targets.Add(MutationTarget.ForArrayItem(array, index, elementName, path, str));
                break;
            default:
                break;
        }
    }

    private static bool TryGetString(JsonValue value, out string result)
    {
        if (value.TryGetValue(out string? str) && str is not null)
        {
            result = str;
            return true;
        }

        result = string.Empty;
        return false;
    }
}
