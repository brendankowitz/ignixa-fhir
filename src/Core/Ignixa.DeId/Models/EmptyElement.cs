// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.DeId.Models;

/// <summary>
/// Represents an empty FHIR resource with only resourceType and meta.security containing REDACTED.
/// </summary>
public static class EmptyElement
{
    public static ResourceJsonNode Create(string resourceType)
    {
        var json = new JsonObject
        {
            ["resourceType"] = resourceType,
            ["meta"] = new JsonObject
            {
                ["security"] = new JsonArray
                {
                    SecurityLabels.REDACT.ToJsonObject()
                }
            }
        };

        return ResourceJsonNode.Parse(json.ToJsonString());
    }

    public static string ToJson(string resourceType, bool pretty = false)
    {
        var json = new JsonObject
        {
            ["resourceType"] = resourceType,
            ["meta"] = new JsonObject
            {
                ["security"] = new JsonArray
                {
                    SecurityLabels.REDACT.ToJsonObject()
                }
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = pretty };
        return json.ToJsonString(options);
    }

    public static bool IsEmptyElement(IElement element)
    {
        var children = element.Children();
        return children.Count == 1 && element.Children("meta").Count == 1;
    }

    public static bool IsEmptyElement(string elementJson)
    {
        try
        {
            var node = JsonNode.Parse(elementJson);
            if (node is not JsonObject obj) return false;
            var children = obj.Where(p => p.Key != "resourceType").ToList();
            return children.Count == 1 && obj.ContainsKey("meta");
        }
        catch
        {
            return false;
        }
    }

    public static bool IsEmpty(object? element)
    {
        return element switch
        {
            string s => IsEmptyElement(s),
            IElement e => IsEmptyElement(e),
            _ => false
        };
    }
}
