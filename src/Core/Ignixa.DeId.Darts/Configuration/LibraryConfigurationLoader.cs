// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text;
using System.Text.Json;
using Ignixa.DeId.Configuration;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.DeId.Darts.Configuration;

public class LibraryConfigurationLoader
{
    private static readonly JsonSerializerOptions DeserializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DeIdOptions LoadFromLibrary(ResourceJsonNode libraryResource)
    {
        var node = libraryResource.MutableNode;

        var contentArray = node["content"]?.AsArray();
        if (contentArray is null || contentArray.Count == 0)
        {
            throw new InvalidOperationException("Library.content is required.");
        }

        var jsonContent = contentArray
            .FirstOrDefault(c =>
                c?["contentType"]?.GetValue<string>() == "application/json")
            ?["data"]
            ?.GetValue<string>();

        if (string.IsNullOrEmpty(jsonContent))
        {
            throw new InvalidOperationException("No application/json attachment found in Library.content.");
        }

        var jsonBytes = Convert.FromBase64String(jsonContent);
        var json = Encoding.UTF8.GetString(jsonBytes);

        DeIdOptions? options;
        try
        {
            options = JsonSerializer.Deserialize<DeIdOptions>(json, DeserializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize DeIdOptions from Library.content.", ex);
        }

        if (options is null)
        {
            throw new InvalidOperationException("Failed to deserialize DeIdOptions from Library.content.");
        }

        return options;
    }

    public static ResourceJsonNode CreateLibraryResource(string id, string policyCode, DeIdOptions options, string? version = null)
    {
        var json = JsonSerializer.Serialize(options, SerializerOptions);

        var bytes = Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes);

        var libraryJson = $$"""
            {
                "resourceType": "Library",
                "id": "{{id}}",
                "status": "active",
                "type": {
                    "coding": [
                        {
                            "system": "{{DartsConstants.LibraryTypeSystem}}",
                            "code": "{{DartsConstants.LibraryTypeCode}}"
                        }
                    ]
                },
                "version": "{{version ?? "1.0.0"}}",
                "identifier": [
                    {
                        "system": "http://hl7.org/fhir/us/darts/CodeSystem/DARTSPolicyIdentifiers",
                        "value": "{{policyCode}}"
                    }
                ],
                "content": [
                    {
                        "contentType": "application/json",
                        "data": "{{base64}}"
                    }
                ]
            }
            """;

        return ResourceJsonNode.Parse(libraryJson);
    }
}
