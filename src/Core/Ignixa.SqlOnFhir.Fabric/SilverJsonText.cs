// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Writes JSON text the way the Silver pipeline's JVM JSON writer does: compact, members in order, and only
/// <c>"</c>, <c>\</c> and control characters escaped (non-ASCII characters are written as themselves).
/// </summary>
internal static class SilverJsonText
{
    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>
    /// The text a string column receives for a non-string source token (an object, array, number or boolean), which
    /// is how <c>extension</c> and <c>contained</c> arrive in Silver. Numbers keep their source text (unverified: the
    /// JVM writer may reformat floating-point numbers).
    /// </summary>
    public static string OfSource(JsonNode node)
    {
        var builder = new StringBuilder();
        WriteSource(builder, node);
        return builder.ToString();
    }

    /// <summary>
    /// The <c>_string</c> companion of a final cell value: every struct field in schema order with explicit nulls,
    /// timestamps at millisecond precision in UTC (unverified session time zone), and decimals in the JVM's canonical
    /// decimal form.
    /// </summary>
    public static string? Companion(JsonNode? value, SilverType type)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        WriteFinal(builder, value, type);
        return builder.ToString();
    }

    private static void WriteSource(StringBuilder builder, JsonNode? node)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject members:
                builder.Append('{');
                var first = true;
                foreach (var (name, member) in members)
                {
                    AppendSeparator(builder, ref first);
                    WriteString(builder, name);
                    builder.Append(':');
                    WriteSource(builder, member);
                }

                builder.Append('}');
                break;
            case JsonArray items:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in items)
                {
                    AppendSeparator(builder, ref firstItem);
                    WriteSource(builder, item);
                }

                builder.Append(']');
                break;
            default:
                WriteSourceValue(builder, node.AsValue());
                break;
        }
    }

    private static void WriteSourceValue(StringBuilder builder, JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                WriteString(builder, value.GetValue<string>());
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            default:
                builder.Append(value.ToJsonString());
                break;
        }
    }

    private static void WriteFinal(StringBuilder builder, JsonNode? value, SilverType type)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        switch (type.Kind)
        {
            case SilverTypeKind.Struct:
                builder.Append('{');
                var first = true;
                foreach (var field in type.Fields)
                {
                    AppendSeparator(builder, ref first);
                    WriteString(builder, field.Name);
                    builder.Append(':');
                    WriteFinal(builder, value[field.Name], field.Type);
                }

                builder.Append('}');
                break;
            case SilverTypeKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in value.AsArray())
                {
                    AppendSeparator(builder, ref firstItem);
                    WriteFinal(builder, item, type.Element!);
                }

                builder.Append(']');
                break;
            case SilverTypeKind.String:
            case SilverTypeKind.Date:
                WriteString(builder, value.GetValue<string>());
                break;
            case SilverTypeKind.Timestamp:
                var instant = SilverTemporal.ParseRowTimestamp(value.GetValue<string>());
                WriteString(builder, SilverTemporal.FormatCompanionTimestamp(instant));
                break;
            case SilverTypeKind.Decimal:
                builder.Append(SilverDecimal.FromRowText(value.ToJsonString(), type.Scale).ToCompanionText());
                break;
            default:
                // Booleans and integers are written by the row encoding exactly as the JVM writes them.
                builder.Append(value.ToJsonString());
                break;
        }
    }

    private static void AppendSeparator(StringBuilder builder, ref bool first)
    {
        if (!first)
        {
            builder.Append(',');
        }

        first = false;
    }

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (var character in text)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case < ' ':
                    builder.Append("\\u00").Append(HexDigits[character >> 4]).Append(HexDigits[character & 0xF]);
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
    }
}
