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

    internal static object? ResolveFilteredList(IResolverContext context, string fieldName)
    {
        var parent = GetParentElement(context);
        if (parent?.ValueKind != JsonValueKind.Object)
            return null;

        if (!parent.Value.TryGetProperty(fieldName, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        IEnumerable<JsonElement> items = value.EnumerateArray().ToList();

        // Apply _offset
        var offsetOpt = context.ArgumentOptional<int?>("_offset");
        if (offsetOpt.HasValue && offsetOpt.Value is > 0)
            items = items.Skip(offsetOpt.Value.Value);

        // Apply _count
        var countOpt = context.ArgumentOptional<int?>("_count");
        if (countOpt.HasValue && countOpt.Value is >= 0)
            items = items.Take(countOpt.Value.Value);

        return items.ToList();
    }

    internal static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return element.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }

    internal static ResourceKey? ParseFhirReference(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
            return null;

        // Skip contained references (#id) and URN references (urn:...)
        if (reference.StartsWith('#') || reference.StartsWith("urn:", StringComparison.Ordinal))
            return null;

        // Handle both relative (Patient/123) and absolute (https://server/fhir/Patient/123)
        var lastSlash = reference.LastIndexOf('/');
        if (lastSlash <= 0)
            return null;

        var id = reference[(lastSlash + 1)..];
        var preceding = reference[..lastSlash];

        var typeSlash = preceding.LastIndexOf('/');
        var resourceType = typeSlash >= 0 ? preceding[(typeSlash + 1)..] : preceding;

        // Validate: resource type should start with uppercase letter
        if (string.IsNullOrEmpty(resourceType) || string.IsNullOrEmpty(id)
            || !char.IsUpper(resourceType[0]))
            return null;

        return new ResourceKey(resourceType, id);
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
