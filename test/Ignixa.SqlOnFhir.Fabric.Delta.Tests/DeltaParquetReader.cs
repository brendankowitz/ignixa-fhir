// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Parquet;
using Parquet.Data;
using Parquet.Meta;
using Parquet.Schema;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Reads the rows of a Delta table's data and change files with Parquet.Net only, so tests can check what the writer
/// committed without trusting the engine that wrote it. Files are chosen by <see cref="DeltaLogReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Rows are assembled from each leaf column's definition and repetition levels rather than through Parquet.Net's
/// object serializer, which drops null list elements and cannot tell a null list from an empty one. Every column of
/// the file appears in every row; an absent value is an explicit JSON <c>null</c>, an empty list is <c>[]</c> and a
/// struct whose fields are all null is an object of nulls.
/// </para>
/// <para>
/// Values are normalized from the file's own Parquet annotations: a UTC microsecond timestamp becomes
/// <c>yyyy-MM-ddTHH:mm:ss.ffffffZ</c>, a date becomes <c>yyyy-MM-dd</c>, a decimal becomes its exact text at the
/// declared scale, integers become JSON numbers and strings stay strings. Decimals are decoded from their unscaled
/// bytes because <see cref="decimal"/> cannot hold 38 digits; Parquet.Net would otherwise round or overflow.
/// </para>
/// </remarks>
internal static class DeltaParquetReader
{
    /// <summary>The rows of every live <c>add</c> file of the table's latest version.</summary>
    public static async Task<IReadOnlyList<JsonObject>> ReadLiveRowsAsync(string tableDirectory)
    {
        var rows = new List<JsonObject>();
        foreach (var file in DeltaLogReader.Read(tableDirectory).LiveFiles.Keys)
        {
            rows.AddRange(await ReadFileAsync(Path.Combine(tableDirectory, file)));
        }

        return rows;
    }

    /// <summary>The change data feed rows, including <c>_change_type</c>, of the <c>cdc</c> files written by one commit.</summary>
    public static async Task<IReadOnlyList<JsonObject>> ReadChangesAsync(string tableDirectory, long version)
    {
        var rows = new List<JsonObject>();
        foreach (var change in DeltaLogReader.Read(tableDirectory).ChangeFiles.Where(change => change.Version == version))
        {
            rows.AddRange(await ReadFileAsync(Path.Combine(tableDirectory, change.Path)));
        }

        return rows;
    }

    public static async Task<IReadOnlyList<JsonObject>> ReadFileAsync(string path)
    {
        using var reader = await ParquetReader.CreateAsync(path);
        var leaves = new List<(DataField Leaf, Field[] Chain)>();
        CollectLeaves(reader.Schema.Fields, [], leaves);

        var rows = new List<JsonObject>();
        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            var groupRows = Enumerable.Range(0, checked((int)rowGroup.RowCount)).Select(_ => new JsonObject()).ToList();
            foreach (var (leaf, chain) in leaves)
            {
                var (column, decimalScale) = await ReadColumnAsync(rowGroup, leaf, chain);
                Assemble(groupRows, chain, column, value => Normalize(leaf, value, decimalScale));
            }

            rows.AddRange(groupRows);
        }

        return rows;
    }

    private static void CollectLeaves(IEnumerable<Field> fields, Field[] ancestors, List<(DataField Leaf, Field[] Chain)> leaves)
    {
        foreach (var field in fields)
        {
            Field[] chain = [.. ancestors, field];
            switch (field)
            {
                case DataField leaf:
                    RequireLevels(leaf, chain);
                    leaves.Add((leaf, chain));
                    break;
                case StructField structField:
                    CollectLeaves(structField.Fields, chain, leaves);
                    break;
                case ListField listField:
                    CollectLeaves([listField.Item], chain, leaves);
                    break;
                default:
                    throw new NotSupportedException($"Parquet field kind {field.SchemaType} is not supported by this reader.");
            }
        }
    }

    // The assembler derives levels from the chain; a mismatch with the file's own levels means the chain is misread.
    private static void RequireLevels(DataField leaf, Field[] chain)
    {
        var definition = chain.Count(IsOptional) + chain.Count(field => field is ListField);
        var repetition = chain.Count(field => field is ListField);
        if (definition != leaf.MaxDefinitionLevel || repetition != leaf.MaxRepetitionLevel)
        {
            throw new InvalidDataException($"Column {leaf.Path} has levels this reader does not derive.");
        }
    }

    private static bool IsOptional(Field field) =>
        (field.SchemaElement ?? throw new InvalidDataException($"Field {field.Name} has no file schema element."))
            .RepetitionType == FieldRepetitionType.OPTIONAL;

    // Decimals are read through a stand-in field of the physical type, with the decimal annotation removed from the
    // file schema, so Parquet.Net neither rounds the value nor overflows decoding the column's statistics.
    private static async Task<(DataColumn Column, int? DecimalScale)> ReadColumnAsync(ParquetRowGroupReader rowGroup, DataField leaf, Field[] chain)
    {
        var element = leaf.SchemaElement!;
        if (element.LogicalType?.DECIMAL is not { } decimalType)
        {
            return (await rowGroup.ReadColumnAsync(leaf), null);
        }

        element.LogicalType = null;
        element.ConvertedType = null;
        var physical = element.Type switch
        {
            Parquet.Meta.Type.INT32 => typeof(int),
            Parquet.Meta.Type.INT64 => typeof(long),
            Parquet.Meta.Type.FIXED_LEN_BYTE_ARRAY or Parquet.Meta.Type.BYTE_ARRAY => typeof(byte[]),
            _ => throw new NotSupportedException($"Decimal column {leaf.Path} has physical type {element.Type}."),
        };

        var standIn = new DataField(leaf.Name, physical, isNullable: IsOptional(leaf));
        Field current = standIn;
        for (var index = chain.Length - 2; index >= 0; index--)
        {
            var ancestor = chain[index];
            current = ancestor switch
            {
                StructField structField => new StructField(structField.Name, current),
                ListField listField => new ListField(listField.Name, current),
                _ => throw new NotSupportedException($"Parquet field kind {ancestor.SchemaType} is not supported by this reader."),
            };
        }

        _ = new ParquetSchema(current);
        if (!standIn.Path.Equals(leaf.Path) ||
            standIn.MaxDefinitionLevel != leaf.MaxDefinitionLevel ||
            standIn.MaxRepetitionLevel != leaf.MaxRepetitionLevel)
        {
            throw new InvalidDataException($"The stand-in for decimal column {leaf.Path} does not match the file.");
        }

        return (await rowGroup.ReadColumnAsync(standIn), decimalType.Scale);
    }

    /// <summary>
    /// Places one leaf column's values into the rows. Each entry's repetition level says which list it continues
    /// (0 starts a new row); its definition level says how deep along the chain it is defined, so a shallower entry is
    /// a null struct, a null list, an empty list or a null element at the point where it stops.
    /// </summary>
    private static void Assemble(List<JsonObject> rows, Field[] chain, DataColumn column, Func<object, JsonNode?> normalize)
    {
        var maxDefinition = chain[^1].MaxDefinitionLevel;
        var count = column.DefinitionLevels?.Length ?? column.DefinedData.Length;
        var listIndexes = new int[chain[^1].MaxRepetitionLevel + 1];
        var row = -1;
        var valueIndex = 0;

        for (var entry = 0; entry < count; entry++)
        {
            var definition = column.DefinitionLevels?[entry] ?? maxDefinition;
            var repetition = column.RepetitionLevels?[entry] ?? 0;
            if (repetition == 0)
            {
                row++;
                System.Array.Clear(listIndexes);
            }
            else
            {
                listIndexes[repetition]++;
                System.Array.Clear(listIndexes, repetition + 1, listIndexes.Length - repetition - 1);
            }

            JsonNode container = rows[row];
            object key = chain[0].Name;
            var defined = 0;
            var depth = 0;
            for (var level = 0; level < chain.Length; level++)
            {
                var field = chain[level];
                if (IsOptional(field) && definition < ++defined)
                {
                    SetNull(container, key);
                    break;
                }

                if (field is StructField)
                {
                    container = GetOrAdd(container, key, () => new JsonObject());
                    key = chain[level + 1].Name;
                }
                else if (field is ListField)
                {
                    container = GetOrAdd(container, key, () => new JsonArray());
                    if (definition < ++defined)
                    {
                        break;
                    }

                    key = listIndexes[++depth];
                }
                else
                {
                    if (definition != maxDefinition)
                    {
                        throw new InvalidDataException($"Column {chain[^1].Path} has an entry defined below its leaf.");
                    }

                    Add(container, key, normalize(column.DefinedData.GetValue(valueIndex++)!));
                }
            }
        }

        if (row + 1 != rows.Count || valueIndex != column.DefinedData.Length)
        {
            throw new InvalidDataException($"Column {chain[^1].Path} does not cover the row group exactly.");
        }
    }

    private static JsonNode GetOrAdd(JsonNode container, object key, Func<JsonNode> create)
    {
        if (TryGet(container, key, out var existing))
        {
            return existing ?? throw new InvalidDataException($"Columns disagree on whether {key} is null.");
        }

        var created = create();
        Add(container, key, created);
        return created;
    }

    private static void SetNull(JsonNode container, object key)
    {
        if (!TryGet(container, key, out var existing))
        {
            Add(container, key, null);
        }
        else if (existing is not null)
        {
            throw new InvalidDataException($"Columns disagree on whether {key} is null.");
        }
    }

    private static bool TryGet(JsonNode container, object key, out JsonNode? existing)
    {
        switch (container, key)
        {
            case (JsonObject obj, string name):
                return obj.TryGetPropertyValue(name, out existing);
            case (JsonArray array, int index) when index < array.Count:
                existing = array[index];
                return true;
            case (JsonArray array, int index) when index == array.Count:
                existing = null;
                return false;
            default:
                throw new InvalidDataException($"Cannot address {key} in the assembled row.");
        }
    }

    private static void Add(JsonNode container, object key, JsonNode? value)
    {
        switch (container, key)
        {
            case (JsonObject obj, string name) when !obj.ContainsKey(name):
                obj.Add(name, value);
                break;
            case (JsonArray array, int index) when index == array.Count:
                array.Add(value);
                break;
            default:
                throw new InvalidDataException($"A value for {key} is already assembled.");
        }
    }

    private static JsonNode? Normalize(DataField leaf, object value, int? decimalScale)
    {
        if (decimalScale is { } scale)
        {
            var unscaled = value switch
            {
                int small => new BigInteger(small),
                long large => new BigInteger(large),
                byte[] bytes => new BigInteger(bytes, isUnsigned: false, isBigEndian: true),
                _ => throw new InvalidDataException($"Decimal column {leaf.Path} decoded as {value.GetType()}."),
            };

            return JsonValue.Create(DecimalText(unscaled, scale));
        }

        var logical = leaf.SchemaElement!.LogicalType;
        if (logical?.TIMESTAMP is { } timestamp)
        {
            if (!timestamp.IsAdjustedToUTC || timestamp.Unit.MICROS is null)
            {
                throw new NotSupportedException($"Timestamp column {leaf.Path} is not UTC microseconds.");
            }

            return JsonValue.Create(((DateTime)value).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
        }

        if (logical?.DATE is not null)
        {
            return JsonValue.Create(((DateTime)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return value switch
        {
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            sbyte or byte or short or ushort or int or long => JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            _ => throw new NotSupportedException($"Column {leaf.Path} decoded as unsupported {value.GetType()}."),
        };
    }

    private static string DecimalText(BigInteger unscaled, int scale)
    {
        var digits = BigInteger.Abs(unscaled).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var text = scale == 0 ? digits : digits[..^scale] + "." + digits[^scale..];
        return unscaled.Sign < 0 ? "-" + text : text;
    }
}
