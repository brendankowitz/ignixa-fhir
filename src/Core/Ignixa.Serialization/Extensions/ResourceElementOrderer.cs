// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Text.Json;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Reorders the properties of raw FHIR resource JSON into StructureDefinition element order.
/// </summary>
/// <remarks>
/// Works on UTF-8 bytes rather than a DOM, so it can order stored resources on read. Each object is
/// indexed into slices of the source buffer, then the slices are written in the order
/// <see cref="ElementOrdering"/> ranks them. Leaf values are copied verbatim, so decimal precision
/// and string escaping survive untouched. Array element order is never changed.
/// For a resource already parsed into a DOM, use <see cref="SourceNodes.ResourceJsonNode.OrderElements"/>.
/// </remarks>
public static class ResourceElementOrderer
{
    /// <summary>
    /// Returns <paramref name="resourceBytes"/> with every object's properties in element order.
    /// </summary>
    /// <param name="resourceBytes">Raw UTF-8 bytes of a FHIR resource JSON object.</param>
    /// <param name="schema">Schema for the resource's FHIR version.</param>
    /// <returns>The input itself when it is already ordered; otherwise a reordered copy.</returns>
    /// <exception cref="JsonException">If the input is malformed or is not a JSON object.</exception>
    public static ReadOnlyMemory<byte> Order(ReadOnlyMemory<byte> resourceBytes, ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        ReadOnlySpan<byte> json = resourceBytes.Span;
        if (IsOrdered(json, type: null, schema))
        {
            return resourceBytes;
        }

        var output = new ArrayBufferWriter<byte>(resourceBytes.Length);
        using (var writer = new Utf8JsonWriter(output))
        {
            WriteObject(json, type: null, schema, writer);
        }

        return output.WrittenMemory;
    }

    private static bool IsOrdered(ReadOnlySpan<byte> json, IType? type, ISchema schema)
    {
        List<Property> properties = IndexObject(json, type, schema);

        for (int i = 0; i < properties.Count; i++)
        {
            Property property = properties[i];
            if (i > 0 && property.Position.Rank < properties[i - 1].Position.Rank)
            {
                return false;
            }

            if (property.Kind != JsonTokenType.None && !IsValueOrdered(json[property.Start..property.End], property.Kind, property.Position.Type, schema))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValueOrdered(ReadOnlySpan<byte> json, JsonTokenType kind, IType? type, ISchema schema)
    {
        if (kind == JsonTokenType.StartObject)
        {
            return IsOrdered(json, type, schema);
        }

        foreach ((int start, int end, JsonTokenType elementKind) in IndexArray(json))
        {
            if (elementKind != JsonTokenType.None && !IsValueOrdered(json[start..end], elementKind, type, schema))
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteObject(ReadOnlySpan<byte> json, IType? type, ISchema schema, Utf8JsonWriter writer)
    {
        List<Property> properties = IndexObject(json, type, schema);

        // List<T>.Sort is unstable; the index tiebreak keeps duplicate and unknown properties in source order.
        properties.Sort(static (left, right) =>
        {
            int byRank = left.Position.Rank.CompareTo(right.Position.Rank);
            return byRank != 0 ? byRank : left.Index.CompareTo(right.Index);
        });

        writer.WriteStartObject();
        foreach (Property property in properties)
        {
            writer.WritePropertyName(property.Name);
            WriteValue(json[property.Start..property.End], property.Kind, property.Position.Type, schema, writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(ReadOnlySpan<byte> json, JsonTokenType kind, IType? type, ISchema schema, Utf8JsonWriter writer)
    {
        switch (kind)
        {
            case JsonTokenType.StartObject:
                WriteObject(json, type, schema, writer);
                break;
            case JsonTokenType.StartArray:
                writer.WriteStartArray();
                foreach ((int start, int end, JsonTokenType elementKind) in IndexArray(json))
                {
                    WriteValue(json[start..end], elementKind, type, schema, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteRawValue(json, skipInputValidation: true);
                break;
        }
    }

    /// <summary>
    /// Indexes the properties of the object at the start of <paramref name="json"/>. An embedded
    /// <c>resourceType</c> overrides <paramref name="type"/>, which is how contained resources,
    /// <c>Bundle.entry.resource</c> and the root resource find their definitions.
    /// </summary>
    private static List<Property> IndexObject(ReadOnlySpan<byte> json, IType? type, ISchema schema)
    {
        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a JSON object but found {reader.TokenType}.");
        }

        var slices = new List<(string Name, int Start, int End, JsonTokenType Kind)>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();
            JsonTokenType tokenType = reader.TokenType;
            int start = (int)reader.TokenStartIndex;

            if (name == ElementOrdering.ResourceTypeProperty && tokenType == JsonTokenType.String)
            {
                type = schema.GetTypeDefinition(reader.GetString()!) ?? type;
            }

            reader.Skip();
            slices.Add((name, start, (int)reader.BytesConsumed, ContainerKind(tokenType)));
        }

        var properties = new List<Property>(slices.Count);
        foreach ((string name, int start, int end, JsonTokenType kind) in slices)
        {
            properties.Add(new Property(name, properties.Count, start, end, kind, ElementOrdering.Resolve(type, name, schema)));
        }

        return properties;
    }

    private static List<(int Start, int End, JsonTokenType Kind)> IndexArray(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        reader.Read();

        var elements = new List<(int Start, int End, JsonTokenType Kind)>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            JsonTokenType tokenType = reader.TokenType;
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            elements.Add((start, (int)reader.BytesConsumed, ContainerKind(tokenType)));
        }

        return elements;
    }

    // Leaves are copied verbatim and never inspected, so only containers keep their token type.
    private static JsonTokenType ContainerKind(JsonTokenType tokenType) =>
        tokenType is JsonTokenType.StartObject or JsonTokenType.StartArray ? tokenType : JsonTokenType.None;

    private readonly record struct Property(string Name, int Index, int Start, int End, JsonTokenType Kind, ElementPosition Position);
}
