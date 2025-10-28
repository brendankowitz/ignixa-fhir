// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Microsoft.IO;

namespace Ignixa.SourceNodeSerialization.Extensions;

/// <summary>
/// Provides streaming normalization of FHIR resources for content comparison.
/// Removes version-specific metadata (meta.versionId and meta.lastUpdated) using
/// zero-buffer streaming JSON processing with pooled memory management.
/// </summary>
public static class ResourceNormalizer
{
    /// <summary>
    /// Static RecyclableMemoryStreamManager for pooled memory allocation.
    /// Shared across all invocations to reduce allocation pressure.
    /// </summary>
    private static readonly RecyclableMemoryStreamManager _memoryStreamManager = new();

    /// <summary>
    /// Removes meta.versionId and meta.lastUpdated fields from a FHIR resource.
    /// Uses streaming to avoid intermediate buffering with pooled memory management.
    /// </summary>
    /// <param name="resourceBytes">Raw UTF-8 bytes of the FHIR resource JSON.</param>
    /// <returns>Resource bytes with meta.versionId and meta.lastUpdated removed.</returns>
    /// <exception cref="JsonException">If input JSON is malformed.</exception>
    public static ReadOnlyMemory<byte> RemoveVersionMetadata(ReadOnlyMemory<byte> resourceBytes)
    {
        var reader = new Utf8JsonReader(resourceBytes.Span);
        using var output = _memoryStreamManager.GetStream("resource-normalization");

        using var writer = new Utf8JsonWriter((Stream)output);
        CopyResourceWithoutVersionMetadata(ref reader, writer);
        writer.Flush();
        output.TryGetBuffer(out ArraySegment<byte> buffer);
        return new ReadOnlyMemory<byte>(buffer.Array!, buffer.Offset, (int)output.Length);
    }

    /// <summary>
    /// Copies a JSON object, skipping meta.versionId and meta.lastUpdated properties.
    /// </summary>
    private static void CopyResourceWithoutVersionMetadata(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    {
                        string propertyName = reader.GetString()!;

                        // Special handling for "meta" property
                        if (propertyName == "meta")
                        {
                            if (!reader.Read())
                            {
                                throw new JsonException("Unexpected end of JSON after property name 'meta'");
                            }

                            // Process meta object, filtering out versionId and lastUpdated
                            CopyMetaWithoutVersion(ref reader, writer);
                        }
                        else
                        {
                            // Copy all other properties as-is
                            if (!reader.Read())
                            {
                                throw new JsonException("Unexpected end of JSON after property name");
                            }

                            writer.WritePropertyName(propertyName);
                            CopyValue(ref reader, writer);
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
    /// Copies a meta object, filtering out versionId and lastUpdated properties.
    /// </summary>
    private static void CopyMetaWithoutVersion(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            // Meta is not an object, just copy it
            writer.WritePropertyName("meta");
            CopyValue(ref reader, writer);
            return;
        }

        writer.WritePropertyName("meta");
        writer.WriteStartObject();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    {
                        string propertyName = reader.GetString()!;

                        // Skip versionId and lastUpdated
                        if (propertyName == "versionId" || propertyName == "lastUpdated")
                        {
                            // Skip this property
                            if (!reader.Read())
                            {
                                throw new JsonException("Unexpected end of JSON after property name");
                            }

                            SkipValue(ref reader);
                        }
                        else
                        {
                            // Copy all other meta properties
                            if (!reader.Read())
                            {
                                throw new JsonException("Unexpected end of JSON after property name");
                            }

                            writer.WritePropertyName(propertyName);
                            CopyValue(ref reader, writer);
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
    /// Copies a single JSON value without modification.
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
                CopyObject(ref reader, writer);
                break;

            case JsonTokenType.StartArray:
                CopyArray(ref reader, writer);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Copies a JSON object without modification.
    /// </summary>
    private static void CopyObject(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                writer.WritePropertyName(reader.GetString()!);
                reader.Read();
                CopyValue(ref reader, writer);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Copies a JSON array without modification.
    /// </summary>
    private static void CopyArray(ref Utf8JsonReader reader, Utf8JsonWriter writer)
    {
        writer.WriteStartArray();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            CopyValue(ref reader, writer);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Skips a JSON value without copying it.
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

            default:
                break;
        }
    }

    /// <summary>
    /// Skips a JSON object.
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
    /// Skips a JSON array.
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
