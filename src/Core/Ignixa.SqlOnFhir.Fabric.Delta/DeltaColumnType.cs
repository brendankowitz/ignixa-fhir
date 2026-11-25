// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Apache.Arrow.Types;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// A Delta column type read from a contract's schema, and its Arrow equivalent. Only the types a Silver projection
/// produces are accepted: map, binary, float, double and <c>timestamp_ntz</c> are rejected, because the projector
/// never emits them and their conversion would be untested.
/// </summary>
internal sealed class DeltaColumnType
{
    private const string ArrowListElementName = "element";

    // A zone-less Arrow timestamp is Delta timestamp_ntz, which needs reader 3 / writer 7; Delta timestamp is UTC.
    private const string ArrowTimestampZone = "UTC";

    private DeltaColumnType(
        DeltaTypeKind kind,
        int precision = 0,
        int scale = 0,
        DeltaColumnType? element = null,
        bool containsNull = false,
        IReadOnlyList<DeltaColumn>? fields = null)
    {
        Kind = kind;
        Precision = precision;
        Scale = scale;
        Element = element;
        ContainsNull = containsNull;
        Fields = fields ?? [];
    }

    public DeltaTypeKind Kind { get; }

    /// <summary>The decimal precision; zero for other kinds.</summary>
    public int Precision { get; }

    /// <summary>The decimal scale; zero for other kinds.</summary>
    public int Scale { get; }

    /// <summary>The array element type; null for other kinds.</summary>
    public DeltaColumnType? Element { get; }

    /// <summary>Whether array elements may be null.</summary>
    public bool ContainsNull { get; }

    /// <summary>The struct fields in schema order; empty for other kinds.</summary>
    public IReadOnlyList<DeltaColumn> Fields { get; }

    /// <summary>Reads the root struct's fields of a contract (each a Delta struct field object) in schema order.</summary>
    /// <exception cref="ArgumentException">A type the writer cannot convert; the message names its schema position.</exception>
    public static ReadOnlyCollection<DeltaColumn> ReadColumns(IReadOnlyList<JsonElement> fields) =>
        Array.AsReadOnly(fields.Select((field, index) => ReadField(field, $"fields[{index}]")).ToArray());

    public IArrowType ToArrowType() => Kind switch
    {
        DeltaTypeKind.String => StringType.Default,
        DeltaTypeKind.Boolean => BooleanType.Default,
        DeltaTypeKind.Byte => Int8Type.Default,
        DeltaTypeKind.Short => Int16Type.Default,
        DeltaTypeKind.Integer => Int32Type.Default,
        DeltaTypeKind.Long => Int64Type.Default,
        DeltaTypeKind.Decimal => new Decimal128Type(Precision, Scale),
        DeltaTypeKind.Date => Date32Type.Default,
        DeltaTypeKind.Timestamp => new TimestampType(TimeUnit.Microsecond, ArrowTimestampZone),
        DeltaTypeKind.Struct => new StructType(Fields.Select(field => field.ToArrowField()).ToList()),
        DeltaTypeKind.Array => new ListType(new Apache.Arrow.Field(ArrowListElementName, Element!.ToArrowType(), ContainsNull)),
        _ => throw new InvalidOperationException("Unhandled Delta type kind."),
    };

    private static DeltaColumn ReadField(JsonElement field, string path) => new(
        field.GetProperty("name").GetString()!,
        Read(field.GetProperty("type"), $"{path}.type"),
        field.GetProperty("nullable").GetBoolean());

    // The contract loader has already checked the schema is well formed, so member access here cannot miss.
    private static DeltaColumnType Read(JsonElement type, string path)
    {
        if (type.ValueKind == JsonValueKind.String)
        {
            return ReadPrimitive(type.GetString()!, path);
        }

        return type.GetProperty("type").GetString() switch
        {
            "struct" => new DeltaColumnType(
                DeltaTypeKind.Struct,
                fields: Array.AsReadOnly(type.GetProperty("fields").EnumerateArray()
                    .Select((field, index) => ReadField(field, $"{path}.fields[{index}]"))
                    .ToArray())),
            "array" => new DeltaColumnType(
                DeltaTypeKind.Array,
                element: Read(type.GetProperty("elementType"), $"{path}.elementType"),
                containsNull: type.GetProperty("containsNull").GetBoolean()),
            _ => throw Unsupported(path),
        };
    }

    private static DeltaColumnType ReadPrimitive(string name, string path)
    {
        switch (name)
        {
            case "string": return new DeltaColumnType(DeltaTypeKind.String);
            case "boolean": return new DeltaColumnType(DeltaTypeKind.Boolean);
            case "byte": return new DeltaColumnType(DeltaTypeKind.Byte);
            case "short": return new DeltaColumnType(DeltaTypeKind.Short);
            case "integer": return new DeltaColumnType(DeltaTypeKind.Integer);
            case "long": return new DeltaColumnType(DeltaTypeKind.Long);
            case "date": return new DeltaColumnType(DeltaTypeKind.Date);
            case "timestamp": return new DeltaColumnType(DeltaTypeKind.Timestamp);
        }

        // The loader accepts only decimal(p,s) with 1 <= p <= 38 and s <= p, the range Decimal128 holds.
        if (name.StartsWith("decimal(", StringComparison.Ordinal) && name.EndsWith(')'))
        {
            var parts = name["decimal(".Length..^1].Split(',');
            return new DeltaColumnType(
                DeltaTypeKind.Decimal,
                precision: int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture),
                scale: int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture));
        }

        throw Unsupported(path);
    }

    private static ArgumentException Unsupported(string path) =>
        new($"The Silver contract column type at schema position {path} is not one the Silver writer converts.");
}
