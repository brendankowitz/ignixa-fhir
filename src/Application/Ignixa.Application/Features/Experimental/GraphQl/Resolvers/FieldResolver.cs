// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Resolvers;
using Ignixa.Abstractions;
using Ignixa.Application.Infrastructure;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.GraphQl.Resolvers;

internal static class FieldResolver
{
    private static readonly FhirPathParser FhirPathExpressionParser = new(preserveTrivia: false);

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

    internal static object? ResolveFilteredList(IResolverContext context, string fieldName, string? instanceType = null)
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
        {
            var fhirVersion = context.Service<IFhirRequestContextAccessor>().RequestContext?.FhirVersion ?? FhirVersion.R4;
            var schemaProvider = context.Service<Func<FhirVersion, IFhirSchemaProvider>>()(fhirVersion);
            items = ApplyFhirPathFilter(items, fhirpathOpt.Value, schemaProvider, instanceType);
        }

        // Apply sub-property filters (e.g., name(use: "official"))
        foreach (var argument in context.Selection.Field.Arguments)
        {
            var argName = argument.Name;
            if (argName is "fhirpath" or "_offset" or "_count" or "url")
                continue;

            var argOpt = context.ArgumentOptional<string?>(argName);
            if (!argOpt.HasValue || string.IsNullOrEmpty(argOpt.Value))
                continue;

            var targetValue = argOpt.Value;
            items = items.Where(e =>
                MatchesSubPropertyFilter(e, argName, targetValue));
        }

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

    internal static IEnumerable<JsonElement> ApplyFhirPathFilter(
        IEnumerable<JsonElement> items, string expression, IFhirSchemaProvider schemaProvider, string? instanceType)
    {
        if (string.IsNullOrEmpty(expression))
            return items;

        // "$index = N" → select element at index N (non-standard shorthand, not a FHIRPath expression)
        if (expression.StartsWith("$index", StringComparison.Ordinal) && expression.Contains('=', StringComparison.Ordinal))
        {
            var indexStr = expression.Split('=', 2)[1].Trim();
            if (int.TryParse(indexStr, out var index))
            {
                var list = items.ToList();
                return index >= 0 && index < list.Count ? [list[index]] : [];
            }
        }

        ValidateFhirPathExpression(expression);

        return items.Where(item =>
        {
            try
            {
                var node = JsonSerializer.SerializeToNode(item);
                if (node is null)
                    return false;

                var sourceNode = JsonNodeSourceNode.Create(node, instanceType ?? "root");
                var element = sourceNode.ToElement(schemaProvider);
                return element.IsTrue(expression);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"Error evaluating FHIRPath expression '{expression}': {ex.Message}")
                        .SetCode("FHIRPATH_INVALID")
                        .SetException(ex)
                        .Build());
            }
        });
    }

    private static void ValidateFhirPathExpression(string expression)
    {
        if (!FhirPathExpressionParser.TryParse(expression, out _, out var errorMessage))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Invalid FHIRPath expression '{expression}': {errorMessage}")
                    .SetCode("FHIRPATH_INVALID")
                    .Build());
        }
    }

    private static bool MatchesSubPropertyFilter(JsonElement element, string propertyName, string targetValue)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (!element.TryGetProperty(propertyName, out var property))
            return false;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() == targetValue,
            JsonValueKind.Number => property.GetRawText() == targetValue,
            JsonValueKind.True => targetValue.Equals("true", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.False => targetValue.Equals("false", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => property.EnumerateArray().Any(a =>
                a.ValueKind switch
                {
                    JsonValueKind.String => a.GetString() == targetValue,
                    JsonValueKind.Number => a.GetRawText() == targetValue,
                    JsonValueKind.True => targetValue.Equals("true", StringComparison.OrdinalIgnoreCase),
                    JsonValueKind.False => targetValue.Equals("false", StringComparison.OrdinalIgnoreCase),
                    _ => false,
                }),
            _ => false,
        };
    }

    internal static IEnumerable<JsonElement> FilterExtensionsByUrl(
        JsonElement parentElement, string? urlFilter)
    {
        if (parentElement.ValueKind != JsonValueKind.Object)
            return [];

        if (!parentElement.TryGetProperty("extension", out var ext) || ext.ValueKind != JsonValueKind.Array)
            return [];

        var items = ext.EnumerateArray().ToList();
        if (string.IsNullOrEmpty(urlFilter))
            return items;

        return items.Where(e =>
            e.TryGetProperty("url", out var urlProp)
            && urlProp.ValueKind == JsonValueKind.String
            && urlProp.GetString() == urlFilter);
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
