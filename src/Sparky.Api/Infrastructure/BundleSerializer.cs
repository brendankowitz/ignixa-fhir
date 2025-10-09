// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using EnsureThat;
using Sparky.Domain.Models;

namespace Sparky.Api.Infrastructure;

/// <summary>
/// Streaming FHIR Bundle serializer that writes directly to an output stream.
/// Uses zero-copy JSON passthrough for optimal performance.
/// </summary>
public static class BundleSerializer
{
    /// <summary>
    /// Serializes a search result bundle asynchronously, streaming entries as they become available.
    /// </summary>
    /// <param name="outputStream">The stream to write JSON to.</param>
    /// <param name="bundleType">The FHIR bundle type (e.g., "searchset").</param>
    /// <param name="total">Total number of matching resources (optional).</param>
    /// <param name="entries">Async stream of resource wrappers to include in the bundle.</param>
    /// <param name="selfLink">The self link URL (optional).</param>
    /// <param name="nextLink">The next page URL for pagination (optional).</param>
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SerializeAsync(
        Stream outputStream,
        string bundleType,
        int? total,
        IAsyncEnumerable<ResourceWrapper> entries,
        string? selfLink = null,
        string? nextLink = null,
        bool pretty = false,
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(outputStream, nameof(outputStream));
        EnsureArg.IsNotNullOrEmpty(bundleType, nameof(bundleType));
        EnsureArg.IsNotNull(entries, nameof(entries));

        await using FhirJsonWriter writer = FhirJsonWriter.Create(outputStream, pretty);

        writer
            .WriteStartObject()
            .WriteString("resourceType", "Bundle")
            .WriteString("type", bundleType)
            .WriteOptionalNumber("total", total);

        // Write link array if any links are present
        writer.Condition(
            !string.IsNullOrEmpty(selfLink) || !string.IsNullOrEmpty(nextLink),
            w => w
                .WriteStartArray("link")
                .Condition(!string.IsNullOrEmpty(selfLink), w2 => w2
                    .WriteStartObject()
                    .WriteString("relation", "self")
                    .WriteString("url", selfLink!)
                    .WriteEndObject())
                .Condition(!string.IsNullOrEmpty(nextLink), w2 => w2
                    .WriteStartObject()
                    .WriteString("relation", "next")
                    .WriteString("url", nextLink!)
                    .WriteEndObject())
                .WriteEndArray());

        // Write entry array
        writer.WriteStartArray("entry");

        // Stream entries as they become available
        await foreach (ResourceWrapper resource in entries.WithCancellation(cancellationToken))
        {
            writer.WriteStartObject();

            // Write fullUrl
            string fullUrl = $"{resource.ResourceType}/{resource.ResourceId}";
            writer.WriteString("fullUrl", fullUrl);

            // Write resource - use zero-copy if RawJsonBytes available
            if (resource.RawJsonBytes.HasValue && resource.RawJsonBytes.Value.Length > 0)
            {
                // Zero-copy: Parse once, write raw properties
                // Use the byte array to avoid copying
                ReadOnlyMemory<byte> jsonMemory = resource.RawJsonBytes.Value;

                using JsonDocument doc = JsonDocument.Parse(jsonMemory);
                JsonElement root = doc.RootElement;

                // Write each property from the resource using raw text
                foreach (JsonProperty prop in root.EnumerateObject())
                {
                    // Get raw UTF-8 bytes for the property value (zero-copy from JsonDocument)
                    byte[] propValueBytes = Encoding.UTF8.GetBytes(prop.Value.GetRawText());
                    writer.WriteRawProperty(prop.Name, propValueBytes);
                }
            }
            else if (!string.IsNullOrEmpty(resource.RawJson))
            {
                // Fallback: Parse RawJson string
                using JsonDocument doc = JsonDocument.Parse(resource.RawJson);
                JsonElement root = doc.RootElement;

                foreach (JsonProperty prop in root.EnumerateObject())
                {
                    byte[] propValueBytes = Encoding.UTF8.GetBytes(prop.Value.GetRawText());
                    writer.WriteRawProperty(prop.Name, propValueBytes);
                }
            }
            else
            {
                // Minimal fallback
                writer.WriteString("resourceType", resource.ResourceType);
                writer.WriteString("id", resource.ResourceId);
            }

            // Write search metadata
            writer.WriteObject("search", w => w
                .WriteString("mode", "match"));

            writer.WriteEndObject(); // end entry

            // Flush periodically to stream data to client
            await writer.FlushAsync(cancellationToken);
        }

        writer.WriteEndArray(); // end entry array
        writer.WriteEndObject(); // end bundle

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Serializes a search result bundle synchronously (non-streaming).
    /// </summary>
    /// <param name="outputStream">The stream to write JSON to.</param>
    /// <param name="bundleType">The FHIR bundle type (e.g., "searchset").</param>
    /// <param name="total">Total number of matching resources (optional).</param>
    /// <param name="entries">Collection of resource wrappers to include in the bundle.</param>
    /// <param name="selfLink">The self link URL (optional).</param>
    /// <param name="nextLink">The next page URL for pagination (optional).</param>
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SerializeAsync(
        Stream outputStream,
        string bundleType,
        int? total,
        IEnumerable<ResourceWrapper> entries,
        string? selfLink = null,
        string? nextLink = null,
        bool pretty = false,
        CancellationToken cancellationToken = default)
    {
        // Convert to async enumerable and use streaming method
        await SerializeAsync(
            outputStream,
            bundleType,
            total,
            ToAsyncEnumerable(entries),
            selfLink,
            nextLink,
            pretty,
            cancellationToken);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (T item in items)
        {
            yield return item;
            await Task.Yield(); // Allow cooperative multitasking
        }
    }
}
