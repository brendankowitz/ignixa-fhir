// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Reads source JSON into a Silver column type, producing the row encoding of the value. One instance serves one
/// projection call; it is not shared across threads.
/// </summary>
/// <remarks>
/// <para>
/// The target type alone decides the shape. Each leaf follows the JSON-reader semantics the Silver pipeline relies
/// on: a string column takes any token (a non-string token becomes its compact JSON text), other primitives need the
/// matching token, and structs and arrays need objects and arrays. A JSON null and a missing property both mean
/// absent. Properties the schema does not name (including the <c>_x</c> primitive-extension shadows) are ignored.
/// The source has already been checked by <see cref="SilverSourceText"/>, so every string and property name decodes
/// to valid Unicode.
/// </para>
/// <para>
/// In <see cref="ReadMode.Source"/>, two Silver rules apply on top of the same-name read: every Reference struct is
/// rewritten by <see cref="SilverReferenceNormalizer"/>, and an <c>xOrig</c> field reads the source value of
/// <c>x</c>. <see cref="ReadMode.Normalized"/> reads normalizer output, which is already in Silver layout, by name only.
/// </para>
/// <para>
/// A null item in an array of structs is rejected (deterministic policy; the source pipeline's behaviour for it is
/// unverified), and so is any null the schema declares non-nullable.
/// </para>
/// </remarks>
internal sealed class SilverValueReader
{
    private readonly SilverReferenceNormalizer _references;

    public SilverValueReader(SilverReferenceNormalizer references)
    {
        _references = references;
    }

    public enum ReadMode
    {
        Source,
        Normalized,
    }

    public static FabricSilverProjectionException Reject(string path, string reason) => new(path, reason);

    public static string Child(string path, string name) => $"{path}.{name}";

    public JsonNode? Read(JsonNode? source, SilverType type, string path, ReadMode mode)
    {
        if (mode == ReadMode.Source && type.IsReference)
        {
            return Read(_references.Normalize(source, path), type, path, ReadMode.Normalized);
        }

        if (source is null)
        {
            return null;
        }

        return type.Kind switch
        {
            SilverTypeKind.Struct => ReadStruct(source, type, path, mode),
            SilverTypeKind.Array => ReadArray(source, type, path, mode),
            SilverTypeKind.String => JsonValue.Create(ReadString(source)),
            _ => ReadScalar(source, type, path),
        };
    }

    /// <summary>The string-column rule: a string token is itself, anything else is its compact JSON text.</summary>
    public static string ReadString(JsonNode source) =>
        source.GetValueKind() == JsonValueKind.String ? source.GetValue<string>() : SilverJsonText.OfSource(source);

    private static JsonNode ReadScalar(JsonNode source, SilverType type, string path)
    {
        var kind = source.GetValueKind();
        switch (type.Kind)
        {
            case SilverTypeKind.Boolean:
                return kind is JsonValueKind.True or JsonValueKind.False
                    ? JsonValue.Create(kind == JsonValueKind.True)
                    : throw Reject(path, "must be a boolean");
            case SilverTypeKind.Date:
                return kind == JsonValueKind.String && SilverTemporal.TryParseDate(source.GetValue<string>(), out var date)
                    ? JsonValue.Create(SilverTemporal.FormatDate(date))
                    : throw Reject(path, "must be a FHIR date");
            case SilverTypeKind.Timestamp:
                return kind == JsonValueKind.String && SilverTemporal.TryParseTimestamp(source.GetValue<string>(), out var instant)
                    ? JsonValue.Create(SilverTemporal.FormatTimestamp(instant))
                    : throw Reject(path, "must be a FHIR date, dateTime or instant with at most microsecond precision");
            case SilverTypeKind.Decimal:
                return kind == JsonValueKind.Number &&
                       SilverDecimal.TryParse(source.ToJsonString(), type.Precision, type.Scale, out var number)
                    ? JsonNode.Parse(number.ToRowText())!
                    : throw Reject(path, $"must be a number that fits decimal({type.Precision},{type.Scale}) without rounding");
            default:
                return kind == JsonValueKind.Number && TryReadInteger(source.ToJsonString(), type.Kind, out var integer)
                    ? JsonValue.Create(integer)
                    : throw Reject(path, "must be an integer within the range of its column type");
        }
    }

    private static bool TryReadInteger(string text, SilverTypeKind kind, out long value)
    {
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return kind switch
        {
            SilverTypeKind.Byte => value is >= sbyte.MinValue and <= sbyte.MaxValue,
            SilverTypeKind.Short => value is >= short.MinValue and <= short.MaxValue,
            SilverTypeKind.Integer => value is >= int.MinValue and <= int.MaxValue,
            _ => true,
        };
    }

    private JsonObject ReadStruct(JsonNode source, SilverType type, string path, ReadMode mode)
    {
        if (source is not JsonObject members)
        {
            throw Reject(path, "must be an object");
        }

        var result = new JsonObject();
        foreach (var field in type.Fields)
        {
            var sourceName = mode == ReadMode.Source ? field.SourceName : field.Name;
            var fieldPath = Child(path, sourceName);
            var value = Read(members[sourceName], field.Type, fieldPath, mode);
            if (value is null && !field.Nullable)
            {
                throw Reject(fieldPath, "is required by the Silver schema");
            }

            result.Add(field.Name, value);
        }

        return result;
    }

    private JsonArray ReadArray(JsonNode source, SilverType type, string path, ReadMode mode)
    {
        if (source is not JsonArray items)
        {
            throw Reject(path, "must be an array");
        }

        var element = type.Element!;
        var result = new JsonArray();
        for (var index = 0; index < items.Count; index++)
        {
            var itemPath = $"{path}[{index}]";
            var item = items[index];
            if (item is null && (element.Kind == SilverTypeKind.Struct || !type.ContainsNull))
            {
                throw Reject(itemPath, "must not be null");
            }

            result.Add(Read(item, element, itemPath, mode));
        }

        return result;
    }
}
