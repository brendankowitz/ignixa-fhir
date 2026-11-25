// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// The column type model the projection is compiled against, parsed once from a validated contract's fields.
/// </summary>
/// <remarks>
/// Instances are immutable. A struct that contains a <see cref="ReferenceMarker"/> field is a Silver Reference: the
/// projection rewrites it as a whole instead of copying source properties into it.
/// </remarks>
internal sealed class SilverType
{
    /// <summary>The field Silver adds to every normalized Reference to keep the original reference text.</summary>
    public const string ReferenceMarker = "msftSourceReference";

    private const string OriginalSuffix = "Orig";

    private static readonly IReadOnlyList<SilverField> NoFields = Array.Empty<SilverField>();

    private static readonly Dictionary<string, SilverType> Primitives = new(StringComparer.Ordinal)
    {
        ["string"] = new(SilverTypeKind.String),
        ["boolean"] = new(SilverTypeKind.Boolean),
        ["byte"] = new(SilverTypeKind.Byte),
        ["short"] = new(SilverTypeKind.Short),
        ["integer"] = new(SilverTypeKind.Integer),
        ["long"] = new(SilverTypeKind.Long),
        ["date"] = new(SilverTypeKind.Date),
        ["timestamp"] = new(SilverTypeKind.Timestamp),
    };

    private SilverType(
        SilverTypeKind kind,
        int precision = 0,
        int scale = 0,
        SilverType? element = null,
        bool containsNull = false,
        IReadOnlyList<SilverField>? fields = null)
    {
        Kind = kind;
        Precision = precision;
        Scale = scale;
        Element = element;
        ContainsNull = containsNull;
        Fields = fields ?? NoFields;
        IsReference = kind == SilverTypeKind.Struct && Fields.Any(field => field.Name == ReferenceMarker);
    }

    public static SilverType String { get; } = Primitives["string"];

    public SilverTypeKind Kind { get; }

    /// <summary>Total digits of a decimal type.</summary>
    public int Precision { get; }

    /// <summary>Fraction digits of a decimal type.</summary>
    public int Scale { get; }

    /// <summary>The item type of an array.</summary>
    public SilverType? Element { get; }

    /// <summary>Whether an array may hold null items.</summary>
    public bool ContainsNull { get; }

    /// <summary>The fields of a struct in schema order; empty for any other kind.</summary>
    public IReadOnlyList<SilverField> Fields { get; }

    public bool IsReference { get; }

    public bool IsComplex => Kind is SilverTypeKind.Struct or SilverTypeKind.Array;

    /// <summary>Parses the top-level columns of a contract whose schema Task 3's loader has already validated.</summary>
    /// <exception cref="InvalidDataException">A column uses a type the projection cannot produce.</exception>
    public static ReadOnlyCollection<SilverField> ParseColumns(IReadOnlyList<JsonElement> fields) =>
        Array.AsReadOnly(ParseFields(fields, "schema"));

    /// <summary>Creates a struct type from fields; used for the internal layouts that only the normalization needs.</summary>
    public static SilverType StructOf(params SilverField[] fields) => new(SilverTypeKind.Struct, fields: fields);

    public SilverField? FindField(string name)
    {
        foreach (var field in Fields)
        {
            if (field.Name == name)
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>Returns a struct identical to this one except that field <paramref name="name"/> has the given type.</summary>
    public SilverType WithFieldType(string name, SilverType type) =>
        StructOf(Fields.Select(field => field.Name == name
            ? new SilverField(field.Name, type, field.Nullable, field.OriginalOf)
            : field).ToArray());

    /// <summary>Structural equality of names, types and nullability, as Delta compares column types.</summary>
    public bool IsSameAs(SilverType other)
    {
        if (Kind != other.Kind || Precision != other.Precision || Scale != other.Scale || ContainsNull != other.ContainsNull)
        {
            return false;
        }

        if (Element is not null && !Element.IsSameAs(other.Element!))
        {
            return false;
        }

        return Fields.Count == other.Fields.Count && Fields.Zip(other.Fields).All(pair =>
            pair.First.Name == pair.Second.Name &&
            pair.First.Nullable == pair.Second.Nullable &&
            pair.First.Type.IsSameAs(pair.Second.Type));
    }

    private static SilverField[] ParseFields(IReadOnlyList<JsonElement> fields, string path)
    {
        var names = fields.Select(field => field.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
        var parsed = new SilverField[fields.Count];
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var name = field.GetProperty("name").GetString()!;
            var type = Parse(field.GetProperty("type"), $"{path}.fields[{index}].type");
            var originalOf = name.Length > OriginalSuffix.Length &&
                             name.EndsWith(OriginalSuffix, StringComparison.Ordinal) &&
                             names.Contains(name[..^OriginalSuffix.Length])
                ? name[..^OriginalSuffix.Length]
                : null;
            parsed[index] = new SilverField(name, type, field.GetProperty("nullable").GetBoolean(), originalOf);
        }

        return parsed;
    }

    private static SilverType Parse(JsonElement type, string path)
    {
        if (type.ValueKind == JsonValueKind.String)
        {
            var name = type.GetString()!;
            if (Primitives.TryGetValue(name, out var primitive))
            {
                return primitive;
            }

            if (name.StartsWith("decimal(", StringComparison.Ordinal))
            {
                var arguments = name["decimal(".Length..^1].Split(',');
                return new SilverType(
                    SilverTypeKind.Decimal,
                    precision: int.Parse(arguments[0], CultureInfo.InvariantCulture),
                    scale: int.Parse(arguments[1], CultureInfo.InvariantCulture));
            }

            // Silver never uses these; projecting them would need JVM float formatting the companions cannot verify.
            throw ContractError.Because($"{path} is a {name} column, which the Silver projection does not support.");
        }

        switch (type.GetProperty("type").GetString())
        {
            case "struct":
                var fields = type.GetProperty("fields").EnumerateArray().ToArray();
                return StructOf(ParseFields(fields, path));
            case "array":
                return new SilverType(
                    SilverTypeKind.Array,
                    element: Parse(type.GetProperty("elementType"), $"{path}.elementType"),
                    containsNull: type.GetProperty("containsNull").GetBoolean());
            default:
                throw ContractError.Because($"{path} is a map column, which a Silver row never holds.");
        }
    }
}
