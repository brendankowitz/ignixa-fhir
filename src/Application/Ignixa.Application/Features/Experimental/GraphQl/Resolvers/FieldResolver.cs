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

    internal static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return element.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }

    internal static IReadOnlyList<JsonElement> GetArrayProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return [];

        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray().ToList();
    }

    internal static JsonElement? GetObjectProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static JsonElement? GetParentElement(IResolverContext context)
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
