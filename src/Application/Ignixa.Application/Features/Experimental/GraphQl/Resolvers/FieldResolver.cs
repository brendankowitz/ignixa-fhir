// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate.Resolvers;
using Ignixa.Application.Features.Experimental.GraphQl.Models;

namespace Ignixa.Application.Features.Experimental.GraphQl.Resolvers;

internal static class FieldResolver
{
    internal static JsonElement ParseResourceBytes(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            span = span[3..];
        return JsonSerializer.Deserialize<JsonElement>(span);
    }

    internal static object? ResolveField(IResolverContext context, string fieldName)
    {
        var parent = GetParentElement(context);
        if (parent?.ValueKind != JsonValueKind.Object)
            return null;

        if (!parent.Value.TryGetProperty(fieldName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => ExtractNumber(value),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => value.EnumerateArray().ToList(),
            JsonValueKind.Object => value,
            _ => null,
        };
    }

    internal static object? ResolveRawJsonField(IResolverContext context, string fieldName)
    {
        var parent = GetParentElement(context);
        if (parent?.ValueKind != JsonValueKind.Object)
            return null;

        return parent.Value.TryGetProperty(fieldName, out var value) ? value : null;
    }

    internal static object? ResolveFilteredList(IResolverContext context, string fieldName)
    {
        var parent = GetParentElement(context);
        if (parent?.ValueKind != JsonValueKind.Object)
            return null;

        if (!parent.Value.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        IEnumerable<JsonElement> items = value.EnumerateArray().ToList();

        // Apply fhirpath filter
        var fhirpathOpt = context.ArgumentOptional<string?>("fhirpath");
        if (fhirpathOpt.HasValue && !string.IsNullOrEmpty(fhirpathOpt.Value))
            items = ApplyFhirPathFilter(items, fhirpathOpt.Value);

        // Apply _offset
        var offsetOpt = context.ArgumentOptional<int?>("_offset");
        if (offsetOpt.HasValue && offsetOpt.Value is > 0)
            items = items.Skip(offsetOpt.Value.Value);

        // Apply _limit
        var countOpt = context.ArgumentOptional<int?>("_limit");
        if (countOpt.HasValue && countOpt.Value is >= 0)
            items = items.Take(countOpt.Value.Value);

        return items.ToList();
    }

    internal static IEnumerable<JsonElement> ApplyFhirPathFilter(
        IEnumerable<JsonElement> items, string expression)
    {
        // "property.exists()" → filter where property exists and is not null
        if (expression.EndsWith(".exists()", StringComparison.Ordinal))
        {
            var propertyName = expression[..^".exists()".Length];
            return items.Where(e =>
                e.TryGetProperty(propertyName, out var v) && v.ValueKind != JsonValueKind.Null);
        }

        // "$index = N" → select element at index N
        if (expression.StartsWith("$index", StringComparison.Ordinal) && expression.Contains('=', StringComparison.Ordinal))
        {
            var indexStr = expression.Split('=', 2)[1].Trim();
            if (int.TryParse(indexStr, out var index))
            {
                var list = items.ToList();
                return index >= 0 && index < list.Count ? [list[index]] : [];
            }
        }

        // "property = 'value'" → simple equality
        if (expression.Contains(" = ", StringComparison.Ordinal) && !expression.Contains("!=", StringComparison.Ordinal))
        {
            var parts = expression.Split(" = ", 2);
            var propName = parts[0].Trim();
            var propValue = parts[1].Trim().Trim('\'', '"');
            return items.Where(e =>
                e.TryGetProperty(propName, out var val)
                && val.ValueKind == JsonValueKind.String
                && val.GetString() == propValue);
        }

        // "property != 'value'" → simple inequality
        if (expression.Contains(" != ", StringComparison.Ordinal))
        {
            var parts = expression.Split(" != ", 2);
            var propName = parts[0].Trim();
            var propValue = parts[1].Trim().Trim('\'', '"');
            return items.Where(e =>
                !e.TryGetProperty(propName, out var val)
                || val.ValueKind != JsonValueKind.String
                || val.GetString() != propValue);
        }

        // Unsupported expressions pass through unfiltered
        return items;
    }

    internal static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return element.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }

    internal static JsonElement? GetParentElement(IResolverContext context)
    {
        var raw = context.Parent<object?>();
        return raw switch
        {
            ChoiceElementValue cv => cv.Element,
            JsonElement je => je,
            _ => null,
        };
    }

    private static object ExtractNumber(JsonElement value)
    {
        if (value.TryGetInt32(out var i)) return i;
        if (value.TryGetInt64(out var l)) return l;
        if (value.TryGetDecimal(out var d)) return d;
        return value.GetDouble();
    }
}
