// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using EnsureThat;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.Bundle.Serialization;

/// <summary>
/// Streaming FHIR Bundle serializer that writes directly to an output stream.
/// Uses zero-copy JSON passthrough for optimal performance.
/// </summary>
public static class StreamingBundleSerializer
{
    /// <summary>
    /// Serializes a search result bundle asynchronously, streaming entries as they become available.
    /// Uses zero-copy serialization with SearchEntryResult (raw bytes from repository).
    /// </summary>
    /// <param name="outputStream">The stream to write JSON to.</param>
    /// <param name="bundleType">The FHIR bundle type (e.g., "searchset").</param>
    /// <param name="total">Total number of matching resources (optional).</param>
    /// <param name="entries">Async stream of search entry results (raw bytes) to include in the bundle.</param>
    /// <param name="selfLink">The self link URL (optional).</param>
    /// <param name="nextLink">The next page URL for pagination (optional).</param>
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SerializeAsync(
        Stream outputStream,
        string bundleType,
        int? total,
        IAsyncEnumerable<SearchEntryResult> entries,
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

        // Stream entries as they become available (zero-copy from raw bytes)
        await foreach (SearchEntryResult resource in entries.WithCancellation(cancellationToken))
        {
            writer.WriteStartObject();

            // Write fullUrl
            string fullUrl = $"{resource.ResourceType}/{resource.ResourceId}";
            writer.WriteString("fullUrl", fullUrl);

            // Write resource - use zero-copy with ResourceBytes
            if (resource.ResourceBytes.Length > 0)
            {
                writer.WriteRawProperty("resource", resource.ResourceBytes);
            }
            else
            {
                // Minimal fallback (should not happen - all SearchEntryResults should have bytes)
                writer.WriteObject("resource",
                    w => w.WriteString("resourceType", resource.ResourceType)
                        .WriteString("id", resource.ResourceId));
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
    /// Uses zero-copy serialization with SearchEntryResult (raw bytes from repository).
    /// </summary>
    /// <param name="outputStream">The stream to write JSON to.</param>
    /// <param name="bundleType">The FHIR bundle type (e.g., "searchset").</param>
    /// <param name="total">Total number of matching resources (optional).</param>
    /// <param name="entries">Collection of search entry results (raw bytes) to include in the bundle.</param>
    /// <param name="selfLink">The self link URL (optional).</param>
    /// <param name="nextLink">The next page URL for pagination (optional).</param>
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SerializeAsync(
        Stream outputStream,
        string bundleType,
        int? total,
        IEnumerable<SearchEntryResult> entries,
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

    /// <summary>
    /// Serializes a bundle response asynchronously with streaming entry responses.
    /// Writes entries as they become available for optimal memory usage.
    /// </summary>
    /// <param name="outputStream">The stream to write JSON to.</param>
    /// <param name="bundleType">The FHIR bundle type (e.g., "batch-response", "transaction-response").</param>
    /// <param name="entryResponses">Async stream of bundle entry responses.</param>
    /// <param name="total">Total number of entries (optional).</param>
    /// <param name="selfLink">The self link URL (optional).</param>
    /// <param name="nextLink">The next page URL (optional).</param>
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SerializeStreamAsync(
        Stream outputStream,
        string bundleType,
        IAsyncEnumerable<BundleEntryResponse> entryResponses,
        int? total = null,
        string? selfLink = null,
        string? nextLink = null,
        bool pretty = false,
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(outputStream, nameof(outputStream));
        EnsureArg.IsNotNullOrEmpty(bundleType, nameof(bundleType));
        EnsureArg.IsNotNull(entryResponses, nameof(entryResponses));

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

        // Stream entry responses as they become available
        await foreach (BundleEntryResponse entryResponse in entryResponses.WithCancellation(cancellationToken))
        {
            WriteEntryResponse(writer, entryResponse);

            // Flush periodically to stream data to client
            await writer.FlushAsync(cancellationToken);
        }

        writer.WriteEndArray(); // end entry array
        writer.WriteEndObject(); // end bundle

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a single bundle entry response to the JSON writer.
    /// </summary>
    private static void WriteEntryResponse(FhirJsonWriter writer, BundleEntryResponse response)
    {
        writer.WriteStartObject();

        // Write response
        writer.WriteStartObject("response");
        writer.WriteString("status", response.Status ?? response.StatusCode.ToString());

        if (!string.IsNullOrEmpty(response.Location))
        {
            writer.WriteString("location", response.Location);
        }

        if (!string.IsNullOrEmpty(response.ETag))
        {
            writer.WriteString("etag", response.ETag);
        }

        if (response.LastModified.HasValue)
        {
            writer.WriteString("lastModified", response.LastModified.Value.ToString("o"));
        }

        writer.WriteEndObject(); // end response

        // Write resource if present
        if (!string.IsNullOrEmpty(response.ResourceJson))
        {
            // Parse and write resource as raw JSON
            byte[] resourceBytes = Encoding.UTF8.GetBytes(response.ResourceJson);
            writer.WriteRawProperty("resource", resourceBytes);
        }

        writer.WriteEndObject(); // end entry
    }
}
