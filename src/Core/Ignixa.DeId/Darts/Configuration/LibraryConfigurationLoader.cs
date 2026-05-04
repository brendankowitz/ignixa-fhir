// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        var resourceType = node["resourceType"]?.GetValue<string>();
        if (!string.Equals(resourceType, "Library", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected resourceType 'Library', but found '{resourceType ?? "null"}'.");
        }

        ValidateLibraryType(node);

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

        byte[] jsonBytes;
        try
        {
            jsonBytes = Convert.FromBase64String(jsonContent);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Library.content data is not valid base64.");
        }

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

        if (string.IsNullOrWhiteSpace(options.FhirVersion))
        {
            throw new InvalidOperationException("DeIdOptions.fhirVersion is required.");
        }

        if (options.Rules.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("DeIdOptions.rules must contain at least one rule.");
        }

        return options;
    }

    private static void ValidateLibraryType(JsonObject node)
    {
        var typeCodingArray = node["type"]?.AsObject()?["coding"]?.AsArray();
        if (typeCodingArray is null)
        {
            throw new InvalidOperationException("Library.type is required and must contain a coding.");
        }

        foreach (var coding in typeCodingArray)
        {
            var system = coding?["system"]?.GetValue<string>();
            var code = coding?["code"]?.GetValue<string>();

            if (string.Equals(system, DartsConstants.LibraryTypeSystem, StringComparison.Ordinal) &&
                string.Equals(code, DartsConstants.LibraryTypeCode, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Library.type must contain coding with system '{DartsConstants.LibraryTypeSystem}' and code '{DartsConstants.LibraryTypeCode}'.");
    }

    public static ResourceJsonNode CreateLibraryResource(string id, string policyCode, DeIdOptions options, string? version = null)
    {
        var json = JsonSerializer.Serialize(options, SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes);

        var library = new
        {
            resourceType = "Library",
            id,
            status = "active",
            type = new
            {
                coding = new[]
                {
                    new { system = DartsConstants.LibraryTypeSystem, code = DartsConstants.LibraryTypeCode }
                }
            },
            version = version ?? "1.0.0",
            identifier = new[]
            {
                new
                {
                    system = "http://hl7.org/fhir/us/darts/CodeSystem/DARTSPolicyIdentifiers",
                    value = policyCode
                }
            },
            content = new[]
            {
                new { contentType = "application/json", data = base64 }
            }
        };

        var libraryJson = JsonSerializer.Serialize(library, SerializerOptions);
        return ResourceJsonNode.Parse(libraryJson);
    }
}
