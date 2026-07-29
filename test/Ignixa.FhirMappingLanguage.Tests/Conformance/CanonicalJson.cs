/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Structural JSON canonicalization for oracle comparison.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Renders JSON in a canonical form so that formatting and object property order
/// do not affect comparison. Array order is preserved because it is semantically
/// significant in FHIR.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Parses <paramref name="json"/> and re-renders it with object properties sorted
    /// by ordinal name.
    /// </summary>
    public static string Canonicalize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        return Sort(node)?.ToJsonString(WriteOptions) ?? "null";
    }

    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var sorted = new JsonObject();
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    sorted[property.Key] = Sort(property.Value?.DeepClone());
                }

                return sorted;
            }

            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var item in array)
                {
                    result.Add(Sort(item?.DeepClone()));
                }

                return result;
            }

            default:
                return node?.DeepClone();
        }
    }
}
