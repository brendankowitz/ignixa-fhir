// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using EnsureThat;
using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// Streams FHIR resources using Utf8JsonReader/Writer, filtering to only requested elements
/// plus mandatory elements (id, meta, resourceType, and schema-defined required fields).
///
/// This is a zero-buffer streaming implementation: as we parse properties from the input,
/// we immediately decide whether to write them to the output based on the element allowlist.
/// </summary>
public static class ResourceElementsSerializer
{
    /// <summary>
    /// Writes a FHIR resource property filtered to only requested elements plus mandatory elements.
    /// Uses streaming to avoid intermediate buffering, writing directly to the provided FhirJsonWriter.
    /// </summary>
    /// <param name="writer">The FhirJsonWriter to write the property to.</param>
    /// <param name="propertyName">The property name (e.g., "resource").</param>
    /// <param name="resourceBytes">Raw UTF-8 bytes of the FHIR resource JSON.</param>
    /// <param name="schemaProvider">Provider for FHIR structure definitions (to identify mandatory fields).</param>
    /// <param name="requestedElements">User-requested element names (dot notation supported for nested).</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
    /// <exception cref="JsonException">If input JSON is malformed.</exception>
    internal static void WriteFilteredResourceProperty(
        FhirJsonWriter writer,
        string propertyName,
        ReadOnlyMemory<byte> resourceBytes,
        ISchema schemaProvider,
        IReadOnlySet<string> requestedElements,
        string resourceType)
    {
        EnsureArg.IsNotNull(writer, nameof(writer));
        EnsureArg.IsNotNullOrEmpty(propertyName, nameof(propertyName));
        EnsureArg.IsNotNull(requestedElements, nameof(requestedElements));
        EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));

        // Build the allowlist: requested elements + mandatory elements
        var allowedElements = BuildAllowedElementsSet(schemaProvider, requestedElements, resourceType);

        // Access the underlying Utf8JsonWriter for low-level JSON streaming
        var underlyingWriter = writer.UnderlyingWriter;

        // Write property name using the underlying writer
        underlyingWriter.WritePropertyName(propertyName);

        // Parse and filter in a single forward-only pass using the underlying JSON writer
        var reader = new Utf8JsonReader(resourceBytes.Span);
        FilterResourceObject(ref reader, underlyingWriter, allowedElements);
    }

    /// <summary>
    /// Builds the set of allowed element names for filtering.
    /// Includes: requested elements + mandatory elements (id, meta, resourceType, required fields from schema).
    /// </summary>
    private static HashSet<string> BuildAllowedElementsSet(
        ISchema schemaProvider,
        IReadOnlySet<string> requestedElements,
        string resourceType)
    {
        var allowed = new HashSet<string>(requestedElements, StringComparer.Ordinal);

        // Normalize FHIR shorthand: _id → id
        if (allowed.Remove("_id"))
        {
            allowed.Add("id");
        }

        // Always include mandatory elements
        allowed.Add("resourceType");
        allowed.Add("id");
        allowed.Add("meta");

        // Add elements marked as required in the schema
        if (schemaProvider != null)
        {
            var schema = schemaProvider.GetTypeDefinition(resourceType);
            if (schema != null)
            {
                foreach (var element in schema.Children)
                {
                    if (element.IsRequired && !allowed.Contains(element.Info.Name))
                    {
                        allowed.Add(element.Info.Name);
                    }
                }
            }
        }

        return allowed;
    }

    /// <summary>
    /// Filters a JSON object, writing only allowed properties to the output writer.
    /// Handles nested objects and arrays by recursively filtering their contents.
    /// </summary>
    private static void FilterResourceObject(
        ref Utf8JsonReader reader,
        Utf8JsonWriter writer,
        HashSet<string> allowedElements)
    {
        writer.WriteStartObject();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    {
                        string propertyName = reader.GetString()!;

                        // Check if this property is in the allowed set
                        if (allowedElements.Contains(propertyName))
                        {
                            // Write this property with all its nested content (root-level filtering only)
                            // First, we need to advance to the value
                            if (!reader.Read())
                            {
                                throw new JsonException("Unexpected end of JSON after property name");
                            }

                            // copyNested=true: include entire subtree unfiltered
                            WritePropertyValue(ref reader, writer, propertyName, copyNested: true);
                        }
                        else
                        {
                            // Skip this property by advancing the reader past its value
                            if (!reader.Read())
                            {
                                throw new JsonException("Unexpected end of JSON after property name");
                            }

                            SkipValue(ref reader);
                        }
                        break;
                    }

                case JsonTokenType.EndObject:
                    writer.WriteEndObject();
                    return;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Writes a single property value to the output writer, advancing the reader past the value.
    /// Handles scalars, objects, and arrays.
    /// </summary>
    private static void WritePropertyValue(
        ref Utf8JsonReader reader,
        Utf8JsonWriter writer,
        string propertyName,
        bool copyNested = false)
    {
        writer.WritePropertyName(propertyName);

        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                writer.WriteNullValue();
                break;

            case JsonTokenType.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonTokenType.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonTokenType.Number:
                {
                    // Read the raw bytes of the number and write them
                    if (reader.TryGetInt64(out long intValue))
                    {
                        writer.WriteNumberValue(intValue);
                    }
                    else if (reader.TryGetDouble(out double doubleValue))
                    {
                        writer.WriteNumberValue(doubleValue);
                    }
                    else
                    {
                        // Fallback: write the raw text
                        var numberText = Encoding.UTF8.GetString(reader.ValueSpan);
                        writer.WriteRawValue(numberText);
                    }
                    break;
                }

            case JsonTokenType.String:
                writer.WriteStringValue(reader.GetString());
                break;

            case JsonTokenType.StartObject:
                if (copyNested)
                {
                    // Copy entire object without filtering
                    CopyObjectValue(ref reader, writer);
                }
                else
                {
                    // Filter nested object (empty allowlist = skip all)
                    FilterResourceObject(ref reader, writer, new HashSet<string>(StringComparer.Ordinal));
                }
                break;

            case JsonTokenType.StartArray:
                writer.WriteStartArray();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            if (copyNested)
                            {
                                CopyObjectValue(ref reader, writer);
                            }
                            else
                            {
                                FilterResourceObject(ref reader, writer, new HashSet<string>(StringComparer.Ordinal));
                            }
                            break;

                        case JsonTokenType.StartArray:
                            // Nested array - copy as-is
                            CopyArrayValue(ref reader, writer);
                            break;

                        case JsonTokenType.Null:
                            writer.WriteNullValue();
                            break;

                        case JsonTokenType.True:
                            writer.WriteBooleanValue(true);
                            break;

                        case JsonTokenType.False:
                            writer.WriteBooleanValue(false);
                            break;

                        case JsonTokenType.Number:
                            if (reader.TryGetInt64(out long intValue))
                            {
                                writer.WriteNumberValue(intValue);
                            }
                            else if (reader.TryGetDouble(out double doubleValue))
                            {
                                writer.WriteNumberValue(doubleValue);
                            }
                            break;

                        case JsonTokenType.String:
                            writer.WriteStringValue(reader.GetString());
                            break;

                        default:
                            break;
                    }
                }
                writer.WriteEndArray();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Copies an entire JSON object without filtering (writes all properties and nested content).
    /// </summary>
    private static void CopyObjectValue(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    writer.WritePropertyName(reader.GetString()!);
                    reader.Read();
                    CopyValue(ref reader, writer);
                    break;

                default:
                    break;
            }
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Copies an entire JSON array without filtering (writes all elements and nested content).
    /// </summary>
    private static void CopyArrayValue(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            CopyValue(ref reader, writer);
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// Copies a single JSON value without filtering (handles scalars, objects, and arrays).
    /// </summary>
    private static void CopyValue(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                writer.WriteNullValue();
                break;

            case JsonTokenType.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonTokenType.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonTokenType.Number:
                if (reader.TryGetInt64(out long intValue))
                {
                    writer.WriteNumberValue(intValue);
                }
                else if (reader.TryGetDouble(out double doubleValue))
                {
                    writer.WriteNumberValue(doubleValue);
                }
                else
                {
                    var numberText = Encoding.UTF8.GetString(reader.ValueSpan);
                    writer.WriteRawValue(numberText);
                }
                break;

            case JsonTokenType.String:
                writer.WriteStringValue(reader.GetString());
                break;

            case JsonTokenType.StartObject:
                CopyObjectValue(ref reader, writer);
                break;

            case JsonTokenType.StartArray:
                CopyArrayValue(ref reader, writer);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Skips a JSON value without writing it, advancing the reader past it.
    /// Handles all JSON types: scalars, objects, arrays, etc.
    /// </summary>
    private static void SkipValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                SkipObject(ref reader);
                break;

            case JsonTokenType.StartArray:
                SkipArray(ref reader);
                break;

            // For scalars (Null, True, False, Number, String), no action needed
            // The reader is already past the value
            default:
                break;
        }
    }

    /// <summary>
    /// Skips an entire JSON object, advancing the reader past the closing brace.
    /// </summary>
    private static void SkipObject(ref Utf8JsonReader reader)
    {
        int depth = 0;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    depth++;
                    break;

                case JsonTokenType.EndObject:
                    if (depth == 0)
                    {
                        return;
                    }
                    depth--;
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Skips an entire JSON array, advancing the reader past the closing bracket.
    /// </summary>
    private static void SkipArray(ref Utf8JsonReader reader)
    {
        int depth = 0;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray:
                    depth++;
                    break;

                case JsonTokenType.EndArray:
                    if (depth == 0)
                    {
                        return;
                    }
                    depth--;
                    break;

                default:
                    break;
            }
        }
    }
}
