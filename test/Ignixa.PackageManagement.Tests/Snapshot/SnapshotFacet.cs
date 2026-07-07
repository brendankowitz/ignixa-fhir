// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.PackageManagement.Tests.Snapshot;

/// <summary>
/// Extracts the validation-relevant facets of a FHIR <c>ElementDefinition</c> into a canonical,
/// comparable string. The shipped-snapshot oracle compares generated elements to package-shipped
/// elements on these facets — <c>min</c>, <c>max</c>, <c>type</c>, <c>binding</c>,
/// <c>fixed[x]</c>, <c>pattern[x]</c> — ignoring purely descriptive or provenance fields
/// (<c>base</c>, <c>mapping</c>, <c>constraint.source</c>, <c>short</c>, …) that snapshot
/// generators are not required to reproduce byte-for-byte.
/// </summary>
internal static class SnapshotFacet
{
    public static (string Path, string? Slice) KeyOf(JsonObject element)
        => (element["path"]?.GetValue<string>() ?? string.Empty, element["sliceName"]?.GetValue<string>());

    public static bool HasSlicing(JsonObject element) => element["slicing"] is not null;

    public static string Describe(JsonObject element)
    {
        var facets = new JsonObject
        {
            ["min"] = element["min"]?.DeepClone(),
            ["max"] = element["max"]?.DeepClone(),
            ["type"] = DescribeTypes(element["type"] as JsonArray),
            ["binding"] = DescribeBinding(element["binding"] as JsonObject),
            ["fixed"] = CollectByPrefix(element, "fixed"),
            ["pattern"] = CollectByPrefix(element, "pattern"),
        };

        return facets.ToJsonString();
    }

    private static JsonArray? DescribeTypes(JsonArray? types)
    {
        if (types is null)
        {
            return null;
        }

        var descriptions = new List<string>();
        foreach (var node in types)
        {
            if (node is JsonObject type)
            {
                descriptions.Add(type.ToJsonString());
            }
        }

        descriptions.Sort(StringComparer.Ordinal);
        var array = new JsonArray();
        foreach (var description in descriptions)
        {
            array.Add(description);
        }

        return array;
    }

    private static JsonObject? DescribeBinding(JsonObject? binding)
    {
        if (binding is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["strength"] = binding["strength"]?.DeepClone(),
            ["valueSet"] = binding["valueSet"]?.DeepClone(),
        };
    }

    private static JsonObject CollectByPrefix(JsonObject element, string prefix)
    {
        var collected = new JsonObject();
        foreach (var property in element)
        {
            if (property.Key.StartsWith(prefix, StringComparison.Ordinal)
                && property.Key.Length > prefix.Length
                && char.IsUpper(property.Key[prefix.Length]))
            {
                collected[property.Key] = property.Value?.DeepClone();
            }
        }

        return collected;
    }
}
