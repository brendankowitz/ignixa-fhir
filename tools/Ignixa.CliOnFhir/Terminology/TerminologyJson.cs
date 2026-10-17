// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Reads the few JSON shapes terminology resources use, tolerating absent members and wrong types as absence.</summary>
internal static class TerminologyJson
{
    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A non-empty string member, or null.</summary>
    public static string? NonEmpty(JsonElement element, string name) => String(element, name) is { Length: > 0 } value ? value : null;

    public static bool? Boolean(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    public static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    public static JsonElement? Object(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    /// <summary>
    /// The value of a <c>value[x]</c> member as text and its type suffix: <c>system|code</c> for a Coding, the literal otherwise;
    /// null when the element has none this tool reads.
    /// </summary>
    public static (string Value, string Type)? ValueX(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!property.Name.StartsWith("value", StringComparison.Ordinal) || property.Name.Length == 5)
            {
                continue;
            }

            string type = property.Name[5..];
            JsonElement value = property.Value;
            string? text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Object when type == "Coding" => $"{String(value, "system")}|{String(value, "code")}",
                _ => null,
            };

            // FHIR names primitive types in lower camel case (code, dateTime) and complex ones in upper (Coding).
            string typeName = type == "Coding" ? type : char.ToLower(type[0], CultureInfo.InvariantCulture) + type[1..];
            return text is null ? null : (text, typeName);
        }

        return null;
    }

    /// <summary>Lower-case hex SHA-256 of the compact form of <paramref name="parts"/>, written in order; missing parts are written as null.</summary>
    public static string Fingerprint(params JsonElement?[] parts)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (JsonElement? part in parts)
            {
                if (part is { } element)
                {
                    element.WriteTo(writer);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }

            writer.WriteEndArray();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    /// <summary>R5 <c>versionAlgorithmString</c>, or the code of <c>versionAlgorithmCoding</c>; null when neither is present.</summary>
    public static string? VersionAlgorithm(JsonElement resource) =>
        NonEmpty(resource, "versionAlgorithmString") ?? (Object(resource, "versionAlgorithmCoding") is { } coding ? NonEmpty(coding, "code") : null);

    /// <summary>A member as a JSON element, or null when absent.</summary>
    public static JsonElement? Member(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : null;
}
