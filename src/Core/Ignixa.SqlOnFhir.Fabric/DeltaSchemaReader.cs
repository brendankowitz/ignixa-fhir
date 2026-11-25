// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Parses a Delta <c>schemaString</c> and checks it is a well-formed struct of the Delta schema serialization.
/// Failure reasons identify nodes by their position in the schema (for example <c>fields[3].type</c>), never by
/// name or value.
/// </summary>
internal static partial class DeltaSchemaReader
{
    private static readonly FrozenSet<string> Primitives = new[]
    {
        "string", "long", "integer", "short", "byte", "float", "double", "boolean", "binary", "date", "timestamp",
        "timestamp_ntz",
    }.ToFrozenSet(StringComparer.Ordinal);

    private const int MaxDecimalPrecision = 38;

    /// <summary>Returns the root struct's fields in schema order, as elements independent of the parsed document.</summary>
    public static ReadOnlyCollection<JsonElement> ReadFields(string schemaString)
    {
        using var document = Parse(schemaString);
        var fields = ReadStruct(document.RootElement, "schema");
        return Array.AsReadOnly(fields.Select(field => field.Clone()).ToArray());
    }

    private static JsonDocument Parse(string schemaString)
    {
        try
        {
            return JsonDocument.Parse(schemaString);
        }
        catch (JsonException)
        {
            // The parser's message can quote input characters, so it is deliberately not chained.
            throw ContractError.Because("the schemaString is not valid JSON within the supported nesting depth.");
        }
    }

    private static List<JsonElement> ReadStruct(JsonElement node, string path)
    {
        RequireTypeTag(node, "struct", path);
        if (!node.TryGetProperty("fields", out var fieldsNode) || fieldsNode.ValueKind != JsonValueKind.Array)
        {
            throw ContractError.Because($"{path} has no fields array.");
        }

        if (fieldsNode.GetArrayLength() == 0)
        {
            throw ContractError.Because($"{path} has no fields.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fields = new List<JsonElement>(fieldsNode.GetArrayLength());
        var index = 0;
        foreach (var field in fieldsNode.EnumerateArray())
        {
            ValidateField(field, $"{path}.fields[{index}]", names);
            fields.Add(field);
            index++;
        }

        return fields;
    }

    private static void ValidateField(JsonElement field, string path, HashSet<string> names)
    {
        if (field.ValueKind != JsonValueKind.Object)
        {
            throw ContractError.Because($"{path} is not an object.");
        }

        if (!field.TryGetProperty("name", out var name) ||
            name.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(name.GetString()))
        {
            throw ContractError.Because($"{path} has no name.");
        }

        if (!names.Add(name.GetString()!))
        {
            throw ContractError.Because($"{path} repeats the name of an earlier field in the same struct.");
        }

        if (!field.TryGetProperty("nullable", out var nullable) ||
            nullable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw ContractError.Because($"{path} has no boolean nullable flag.");
        }

        if (field.TryGetProperty("metadata", out var metadata) && metadata.ValueKind != JsonValueKind.Object)
        {
            throw ContractError.Because($"{path}.metadata is not an object.");
        }

        // An absent type leaves an Undefined element, which ValidateType rejects.
        field.TryGetProperty("type", out var type);
        ValidateType(type, $"{path}.type");
    }

    private static void ValidateType(JsonElement type, string path)
    {
        switch (type.ValueKind)
        {
            case JsonValueKind.String:
                ValidatePrimitive(type.GetString()!, path);
                break;
            case JsonValueKind.Object:
                ValidateComplex(type, path);
                break;
            default:
                throw ContractError.Because($"{path} is neither a primitive name nor a complex type object.");
        }
    }

    private static void ValidatePrimitive(string name, string path)
    {
        if (Primitives.Contains(name))
        {
            return;
        }

        var match = DecimalPattern().Match(name);
        if (match.Success)
        {
            var precision = int.Parse(match.Groups["precision"].Value, CultureInfo.InvariantCulture);
            var scale = int.Parse(match.Groups["scale"].Value, CultureInfo.InvariantCulture);
            if (precision is >= 1 and <= MaxDecimalPrecision && scale <= precision)
            {
                return;
            }
        }

        throw ContractError.Because($"{path} is not a supported primitive type.");
    }

    private static void ValidateComplex(JsonElement type, string path)
    {
        var tag = type.TryGetProperty("type", out var tagNode) && tagNode.ValueKind == JsonValueKind.String
            ? tagNode.GetString()
            : null;

        switch (tag)
        {
            case "struct":
                ReadStruct(type, path);
                break;
            case "array":
                ValidateMember(type, "elementType", path);
                RequireBoolean(type, "containsNull", path);
                break;
            case "map":
                ValidateMember(type, "keyType", path);
                ValidateMember(type, "valueType", path);
                RequireBoolean(type, "valueContainsNull", path);
                break;
            default:
                throw ContractError.Because($"{path} is not a struct, array or map type.");
        }
    }

    private static void ValidateMember(JsonElement type, string member, string path)
    {
        // An absent member leaves an Undefined element, which ValidateType rejects.
        type.TryGetProperty(member, out var memberType);
        ValidateType(memberType, $"{path}.{member}");
    }

    private static void RequireBoolean(JsonElement type, string member, string path)
    {
        if (!type.TryGetProperty(member, out var flag) || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw ContractError.Because($"{path} has no boolean {member} flag.");
        }
    }

    private static void RequireTypeTag(JsonElement node, string expected, string path)
    {
        if (node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("type", out var tag) ||
            tag.ValueKind != JsonValueKind.String ||
            tag.GetString() != expected)
        {
            throw ContractError.Because($"{path} is not a {expected} type.");
        }
    }

    [GeneratedRegex("^decimal\\((?<precision>[0-9]{1,3}),(?<scale>[0-9]{1,3})\\)$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();
}
