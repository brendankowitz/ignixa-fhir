// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using Apache.Arrow;
using Apache.Arrow.Types;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Converts projected Silver rows to one Arrow record batch, column by column, driven by the contract's columns.
/// A JSON <c>null</c> cell is an Arrow null at every level, so a null struct stays distinct from a struct whose fields
/// are all null, a null array from an empty one, and null elements of primitive arrays are kept. A cell whose JSON
/// kind does not match its column fails with the column path, never the value.
/// </summary>
internal static class SilverArrowConverter
{
    private const int Decimal128Width = 16;

    private static readonly JsonElement NullCell = JsonDocument.Parse("null").RootElement.Clone();

    public static RecordBatch Convert(IReadOnlyList<DeltaColumn> columns, Schema schema, IReadOnlyList<FabricSilverRow> rows)
    {
        var arrays = columns
            .Select((column, index) => Build(column.Type, rows.Select(row => row.Cells[index]).ToList(), column.Name))
            .ToList();
        return new RecordBatch(schema, arrays, rows.Count);
    }

    private static IArrowArray Build(DeltaColumnType type, IReadOnlyList<JsonElement> cells, string path) => type.Kind switch
    {
        DeltaTypeKind.String => BuildString(cells, path),
        DeltaTypeKind.Boolean => BuildBoolean(cells, path),
        DeltaTypeKind.Byte => BuildFixed(type, cells, path, cell => Integer(cell, path, sbyte.MinValue, sbyte.MaxValue, value => (sbyte)value)),
        DeltaTypeKind.Short => BuildFixed(type, cells, path, cell => Integer(cell, path, short.MinValue, short.MaxValue, value => (short)value)),
        DeltaTypeKind.Integer => BuildFixed(type, cells, path, cell => Integer(cell, path, int.MinValue, int.MaxValue, value => (int)value)),
        DeltaTypeKind.Long => BuildFixed(type, cells, path, cell => Integer(cell, path, long.MinValue, long.MaxValue, value => value)),
        DeltaTypeKind.Date => BuildFixed(type, cells, path, cell => SilverTransitValues.DaysSinceEpoch(SilverTransitValues.Date(cell, path))),
        DeltaTypeKind.Timestamp => BuildFixed(type, cells, path, cell => SilverTransitValues.TimestampMicroseconds(cell, path)),
        DeltaTypeKind.Decimal => BuildDecimal(type, cells, path),
        DeltaTypeKind.Struct => BuildStruct(type, cells, path),
        DeltaTypeKind.Array => BuildList(type, cells, path),
        _ => throw new InvalidOperationException("Unhandled Delta type kind."),
    };

    private static StringArray BuildString(IReadOnlyList<JsonElement> cells, string path)
    {
        var builder = new StringArray.Builder();
        foreach (var cell in cells)
        {
            if (IsNull(cell))
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(cell.ValueKind == JsonValueKind.String ? cell.GetString() : throw WrongKind(path, "a string"));
            }
        }

        return builder.Build();
    }

    private static BooleanArray BuildBoolean(IReadOnlyList<JsonElement> cells, string path)
    {
        var builder = new BooleanArray.Builder();
        foreach (var cell in cells)
        {
            if (IsNull(cell))
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(cell.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => throw WrongKind(path, "a boolean"),
                });
            }
        }

        return builder.Build();
    }

    private static IArrowArray BuildFixed<T>(DeltaColumnType type, IReadOnlyList<JsonElement> cells, string path, Func<JsonElement, T> read)
        where T : struct
    {
        var values = new ArrowBuffer.Builder<T>(cells.Count);
        var validity = new ArrowBuffer.BitmapBuilder(cells.Count);
        foreach (var cell in cells)
        {
            var present = !IsNull(cell);
            values.Append(present ? read(cell) : default);
            validity.Append(present);
        }

        return Assemble(type, cells.Count, validity, [values.Build()]);
    }

    private static IArrowArray BuildDecimal(DeltaColumnType type, IReadOnlyList<JsonElement> cells, string path)
    {
        var values = new ArrowBuffer.Builder<byte>(cells.Count * Decimal128Width);
        var validity = new ArrowBuffer.BitmapBuilder(cells.Count);
        Span<byte> bytes = stackalloc byte[Decimal128Width];
        foreach (var cell in cells)
        {
            var present = !IsNull(cell);
            var unscaled = present ? SilverTransitValues.DecimalUnscaled(cell, type.Precision, type.Scale, path) : BigInteger.Zero;
            WriteDecimal128(unscaled, bytes);
            values.Append(bytes);
            validity.Append(present);
        }

        return Assemble(type, cells.Count, validity, [values.Build()]);
    }

    // Decimal128 is a 16-byte little-endian two's complement integer; |unscaled| < 10^38 always fits.
    private static void WriteDecimal128(BigInteger unscaled, Span<byte> destination)
    {
        destination.Fill(unscaled.Sign < 0 ? (byte)0xFF : (byte)0x00);
        unscaled.TryWriteBytes(destination, out _, isUnsigned: false, isBigEndian: false);
    }

    private static IArrowArray BuildStruct(DeltaColumnType type, IReadOnlyList<JsonElement> cells, string path)
    {
        var validity = new ArrowBuffer.BitmapBuilder(cells.Count);
        foreach (var cell in cells)
        {
            var present = !IsNull(cell);
            if (present && cell.ValueKind != JsonValueKind.Object)
            {
                throw WrongKind(path, "an object");
            }

            validity.Append(present);
        }

        var children = type.Fields
            .Select(field => Build(
                field.Type,
                cells.Select(cell => IsNull(cell) ? NullCell : Member(cell, field.Name, path)).ToList(),
                $"{path}.{field.Name}"))
            .ToList();
        var nullCount = validity.UnsetBitCount;
        return new StructArray(type.ToArrowType(), cells.Count, children, validity.Build(), nullCount);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned list array owns its element array.")]
    private static IArrowArray BuildList(DeltaColumnType type, IReadOnlyList<JsonElement> cells, string path)
    {
        var offsets = new ArrowBuffer.Builder<int>(cells.Count + 1);
        var validity = new ArrowBuffer.BitmapBuilder(cells.Count);
        var elements = new List<JsonElement>();
        offsets.Append(0);
        foreach (var cell in cells)
        {
            var present = !IsNull(cell);
            if (present)
            {
                if (cell.ValueKind != JsonValueKind.Array)
                {
                    throw WrongKind(path, "an array");
                }

                elements.AddRange(cell.EnumerateArray());
            }

            offsets.Append(elements.Count);
            validity.Append(present);
        }

        var child = Build(type.Element!, elements, $"{path}[]");
        var nullCount = validity.UnsetBitCount;
        return new ListArray(type.ToArrowType(), cells.Count, offsets.Build(), child, validity.Build(), nullCount);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned array owns the array data.")]
    private static IArrowArray Assemble(DeltaColumnType type, int length, ArrowBuffer.BitmapBuilder validity, ArrowBuffer[] valueBuffers)
    {
        var nullCount = validity.UnsetBitCount;
        var data = new ArrayData(type.ToArrowType(), length, nullCount, offset: 0, [validity.Build(), .. valueBuffers]);
        return ArrowArrayFactory.BuildArray(data);
    }

    private static T Integer<T>(JsonElement cell, string path, long minimum, long maximum, Func<long, T> narrow)
    {
        if (cell.ValueKind != JsonValueKind.Number || !cell.TryGetInt64(out var value) || value < minimum || value > maximum)
        {
            throw WrongKind(path, "an integer in the column's range");
        }

        return narrow(value);
    }

    // The projector writes every struct field, explicit null included; a missing member means a different contract.
    private static JsonElement Member(JsonElement cell, string name, string path) =>
        cell.TryGetProperty(name, out var member)
            ? member
            : throw new InvalidDataException($"The projected struct at '{path}' has no field '{name}'; the projection does not match the Silver contract.");

    private static bool IsNull(JsonElement cell) => cell.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    private static InvalidDataException WrongKind(string path, string expected) =>
        new($"The projected cell at '{path}' is not {expected}; the projection does not match the Silver contract.");
}
