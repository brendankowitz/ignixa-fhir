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
/// Every field, struct, array and map object must carry exactly the members that serialization defines: the Delta
/// engine refuses a log whose schema lacks one (for example a field's <c>metadata</c>) and drops any other, so a table
/// created from a schema with a missing or extra member could not be loaded, or would not match its own contract,
/// afterwards. Failure reasons identify nodes by their position in the schema (for example <c>fields[3].type</c>),
/// never by name or value.
/// </summary>
internal static partial class DeltaSchemaReader
{
    private static readonly FrozenSet<string> Primitives = new[]
    {
        "string", "long", "integer", "short", "byte", "float", "double", "boolean", "binary", "date", "timestamp",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> FieldMembers = Members("name", "type", "nullable", "metadata");
    private static readonly FrozenSet<string> StructMembers = Members("type", "fields");
    private static readonly FrozenSet<string> ArrayMembers = Members("type", "elementType", "containsNull");
    private static readonly FrozenSet<string> MapMembers = Members("type", "keyType", "valueType", "valueContainsNull");

    // Delta marks these as needing more than protocol 1/4 (column mapping is reader 2 / writer 5, identity columns are
    // writer 6), so a field carrying them cannot belong to a 1/4 table.
    private static readonly string[] UnsupportedFieldMetadataPrefixes = ["delta.columnMapping.", "delta.identity."];

    private const int MaxDecimalPrecision = 38;

    /// <summary>Returns the root struct's fields in schema order, as elements independent of the parsed document.</summary>
    public static ReadOnlyCollection<JsonElement> ReadFields(string schemaString)
    {
        using var document = StrictJson.Parse(schemaString, "the schemaString");
        var fields = ReadStruct(document.RootElement, "schema");
        return Array.AsReadOnly(fields.Select(field => field.Clone()).ToArray());
    }

    private static List<JsonElement> ReadStruct(JsonElement node, string path)
    {
        RequireTypeTag(node, "struct", path);
        RequireOnlyDefinedMembers(node, StructMembers, "struct type", path);
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

        RequireOnlyDefinedMembers(field, FieldMembers, "struct field", path);

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

        if (!field.TryGetProperty("metadata", out var metadata))
        {
            throw ContractError.Because($"{path} has no metadata object.");
        }

        RequireSupportedMetadata(metadata, $"{path}.metadata");

        // An absent type leaves an Undefined element, which ValidateType rejects.
        field.TryGetProperty("type", out var type);
        ValidateType(type, $"{path}.type");
    }

    private static void RequireSupportedMetadata(JsonElement metadata, string path)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            throw ContractError.Because($"{path} is not an object.");
        }

        foreach (var property in metadata.EnumerateObject())
        {
            if (UnsupportedFieldMetadataPrefixes.Any(prefix => property.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                throw ContractError.Because($"{path} carries a property that needs more than protocol 1/4.");
            }
        }
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

        throw ContractError.Because($"{path} is not a primitive type supported under protocol 1/4.");
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
                RequireOnlyDefinedMembers(type, ArrayMembers, "array type", path);
                ValidateMember(type, "elementType", path);
                RequireBoolean(type, "containsNull", path);
                break;
            case "map":
                RequireOnlyDefinedMembers(type, MapMembers, "map type", path);
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

    // Missing members are reported by the checks that read them; this rejects the members nothing reads.
    private static void RequireOnlyDefinedMembers(JsonElement node, FrozenSet<string> members, string kind, string path)
    {
        if (node.EnumerateObject().Any(property => !members.Contains(property.Name)))
        {
            throw ContractError.Because($"{path} has a member that a Delta {kind} does not define.");
        }
    }

    private static FrozenSet<string> Members(params string[] names) => names.ToFrozenSet(StringComparer.Ordinal);

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
