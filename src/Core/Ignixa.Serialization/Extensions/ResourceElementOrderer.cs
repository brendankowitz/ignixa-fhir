// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Reorders the properties of a FHIR JSON resource into StructureDefinition element order - the
/// order the Firely SDK serializer emits.
/// </summary>
/// <remarks>
/// Works on raw UTF-8 bytes rather than a DOM. Each object is indexed into slices of the source
/// buffer, then the slices are written in element order. Leaf values are copied verbatim, so decimal
/// precision and string escaping survive untouched. Ordering rules:
/// <list type="bullet">
/// <item><c>resourceType</c> first, then elements by <see cref="IType.Order"/>.</item>
/// <item>Choice variants (<c>valueQuantity</c>) take the position of their choice element (<c>value[x]</c>).</item>
/// <item>A primitive's extension sibling (<c>_birthDate</c>) immediately follows the primitive.</item>
/// <item>Properties the schema does not describe go last, in their original relative order.</item>
/// <item>Array element order is never changed.</item>
/// </list>
/// </remarks>
public static class ResourceElementOrderer
{
    private const int ResourceTypeRank = -1;
    private const int UnknownRank = int.MaxValue;

    private static readonly ConditionalWeakTable<ISchema, ConcurrentDictionary<(IType Parent, string Name), ChildResolution>> Resolutions = new();

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
            if (i > 0 && property.Rank < properties[i - 1].Rank)
            {
                return false;
            }

            if (property.Kind != JsonTokenType.None && !IsValueOrdered(json[property.Start..property.End], property.Kind, property.Type, schema))
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
            int byRank = left.Rank.CompareTo(right.Rank);
            return byRank != 0 ? byRank : left.Index.CompareTo(right.Index);
        });

        writer.WriteStartObject();
        foreach (Property property in properties)
        {
            writer.WritePropertyName(property.Name);
            WriteValue(json[property.Start..property.End], property.Kind, property.Type, schema, writer);
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

        var properties = new List<Property>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();
            JsonTokenType tokenType = reader.TokenType;
            int start = (int)reader.TokenStartIndex;

            if (name == "resourceType" && tokenType == JsonTokenType.String)
            {
                type = schema.GetTypeDefinition(reader.GetString()!) ?? type;
            }

            reader.Skip();
            int end = (int)reader.BytesConsumed;
            properties.Add(new Property(name, properties.Count, start, end, ContainerKind(tokenType), Rank: 0, Type: null));
        }

        for (int i = 0; i < properties.Count; i++)
        {
            ChildResolution resolution = Resolve(type, properties[i].Name, schema);
            properties[i] = properties[i] with { Rank = resolution.Rank, Type = resolution.Type };
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

    private static ChildResolution Resolve(IType? parent, string name, ISchema schema)
    {
        if (name == "resourceType")
        {
            return new ChildResolution(ResourceTypeRank, Type: null);
        }

        if (parent is null)
        {
            return new ChildResolution(UnknownRank, Type: null);
        }

        return Resolutions.GetOrCreateValue(schema).GetOrAdd((parent, name), key => ComputeResolution(key.Parent, key.Name, schema));
    }

    private static ChildResolution ComputeResolution(IType parent, string name, ISchema schema)
    {
        bool isExtensionSibling = name.Length > 1 && name[0] == '_';
        string elementName = isExtensionSibling ? name[1..] : name;

        // Exact names win over choice prefixes: "valueSet" must not resolve as a "value[x]" variant.
        IType? element = parent.Children.FirstOrDefault(child => child.Info.Name == elementName);
        string? choiceTypeName = null;
        if (element is null)
        {
            element = parent.Children.FirstOrDefault(child =>
                child.Info.IsChoiceElement
                && elementName.Length > child.Info.Name.Length
                && elementName.StartsWith(child.Info.Name, StringComparison.Ordinal));
            choiceTypeName = element is null ? null : elementName[element.Info.Name.Length..];
        }

        if (element is null)
        {
            return new ChildResolution(UnknownRank, Type: null);
        }

        int rank = (element.Order * 2) + (isExtensionSibling ? 1 : 0);
        IType? type = isExtensionSibling
            ? schema.GetTypeDefinition("Element")
            : choiceTypeName is not null
                ? schema.GetTypeDefinition(choiceTypeName)
                : ResolveElementType(parent, element, schema);

        return new ChildResolution(rank, type);
    }

    private static IType? ResolveElementType(IType parent, IType element, ISchema schema)
    {
        // A BackboneElement is registered under its qualified path, e.g. "Patient.contact".
        IType? backbone = schema.GetTypeDefinition($"{parent.Info.Name}.{element.Info.Name}");
        if (backbone is not null)
        {
            return backbone;
        }

        if (element is not ITypeExtended extended)
        {
            return schema.GetTypeDefinition(element.Info.Name);
        }

        // Recursive backbones (Questionnaire.item.item) declare no type, only "#Questionnaire.item".
        if (extended.ContentReference is { Length: > 1 } contentReference)
        {
            return schema.GetTypeDefinition(contentReference[(contentReference.IndexOf('#', StringComparison.Ordinal) + 1)..]);
        }

        string? typeName = extended.DefaultTypeName ?? (extended.Types.Count > 0 ? extended.Types[0].Code : null);
        return typeName is null ? null : schema.GetTypeDefinition(typeName);
    }

    private readonly record struct Property(string Name, int Index, int Start, int End, JsonTokenType Kind, int Rank, IType? Type);

    private readonly record struct ChildResolution(int Rank, IType? Type);
}
