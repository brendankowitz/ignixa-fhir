// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Text;
using System.Text.Json;
using EnsureThat;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Specification;
using Ignixa.Abstractions;
using ISchema = Ignixa.Abstractions.ISchema;
using FhirBundleLink = Ignixa.Models.BundleLink;

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

        var entryBuffer = new ArrayBufferWriter<byte>();
        await using FhirJsonWriter writer = FhirJsonWriter.Create(outputStream, pretty);
        await using FhirJsonWriter entryWriter = FhirJsonWriter.Create(entryBuffer, pretty);

        try
        {
            WriteBundleHeader(writer, bundleType, total);

            WriteBundleLinksFromStrings(writer, selfLink, nextLink);

            writer.WriteStartArray("entry");

            // Stream entries as they become available (zero-copy from raw bytes)
            await foreach (SearchEntryResult resource in entries.WithCancellation(cancellationToken))
            {
                WriteBufferedSimpleEntry(writer, entryWriter, entryBuffer, resource);

                // Flush periodically to stream data to client
                await writer.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            if (writer.UnderlyingWriter.BytesCommitted == 0)
            {
                DiscardTierOneBuffer(writer);
                throw;
            }

            await CloseSimpleErrorBundleAsync(writer);
            throw;
        }
        catch (Exception ex)
        {
            if (writer.UnderlyingWriter.BytesCommitted == 0)
            {
                DiscardTierOneBuffer(writer);
                throw;
            }

            WriteOperationOutcomeEntry(
                writer,
                new IssueComponent("fatal", "exception", Diagnostics: $"Bundle serialization failed: {ex.Message}"),
                bundleType,
                FhirVersion.R4,
                ErrorEntryFullUrl,
                string.Empty);

            await CloseSimpleErrorBundleAsync(writer);
            throw;
        }

        // Write bundle footer
        await WriteBundleFooterAsync(writer, cancellationToken);
    }

    /// <summary>
    /// Writes one complete entry into the scratch writer, then copies the finished bytes into the
    /// response writer as a single raw array element. Mirrors <see cref="WriteBufferedEntry"/> without
    /// the pagination path's element filtering, which <see cref="SerializeAsync"/> does not support.
    /// Staging keeps the response writer between complete entries at all times, so a mid-entry failure
    /// dirties only the scratch buffer and the bundle stays closable.
    /// </summary>
    private static void WriteBufferedSimpleEntry(
        FhirJsonWriter writer,
        FhirJsonWriter entryWriter,
        ArrayBufferWriter<byte> entryBuffer,
        SearchEntryResult resource)
    {
        entryWriter.WriteStartObject();

        string fullUrl = $"{resource.ResourceType}/{resource.ResourceId}";
        entryWriter.WriteString("fullUrl", fullUrl);

        WriteResourceBytes(entryWriter, resource);

        // CA1308 suppressed: JSON requires lowercase values for FHIR compliance
#pragma warning disable CA1308
        entryWriter.WriteObject("search", w => w
            .WriteString("mode", resource.SearchMode.ToString().ToLowerInvariant()));
#pragma warning restore CA1308

        entryWriter.WriteEndObject();

        // The scratch buffer holds nothing until the writer is flushed into it.
        entryWriter.UnderlyingWriter.Flush();
        writer.UnderlyingWriter.WriteRawValue(entryBuffer.WrittenSpan, skipInputValidation: true);
        entryBuffer.Clear();
        entryWriter.UnderlyingWriter.Reset(entryBuffer);
    }

    /// <summary>
    /// Completes a bundle whose response has already started, then leaves the caller to rethrow.
    /// No links are re-emitted: <see cref="SerializeAsync"/> writes them in the prologue, inside the
    /// guard, so they already survived by the time a failure reaches this catch.
    /// The flush deliberately uses <see cref="CancellationToken.None"/> - an already-canceled token
    /// makes FlushAsync throw immediately, which would defeat the body completion.
    /// </summary>
    private static async Task CloseSimpleErrorBundleAsync(FhirJsonWriter writer)
    {
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(CancellationToken.None);
    }

    /// <summary>
    /// Tier 1: discards everything the main writer has buffered and re-points it at
    /// <see cref="Stream.Null"/> before the caller rethrows.
    /// Retargeting is load-bearing, not tidiness: <see cref="Utf8JsonWriter"/> disposal flushes its
    /// destination stream unconditionally - even with zero bytes pending - and an empty flush on
    /// <c>Response.Body</c> starts the response in Kestrel. Since disposal runs during the unwind,
    /// before the exception reaches FhirExceptionMiddleware, a plain <c>Reset()</c> would commit a
    /// headers-only HTTP 200 and the middleware's <c>HasStarted</c> guard would then decline to
    /// write the real status-coded error.
    /// </summary>
    private static void DiscardTierOneBuffer(FhirJsonWriter writer)
    {
        writer.UnderlyingWriter.Reset(Stream.Null);
    }

    /// <summary>
    /// Flush the writer to the output stream when its pending buffer exceeds this size.
    /// Prevents unbounded memory growth for large result sets without flushing on every entry.
    /// </summary>
    private const int FlushThresholdBytes = 50 * 1024 * 1024; // 50 MB

    /// <summary>
    /// fullUrl carried by the mid-stream fatal OperationOutcome entry. A well-formed UUID URN, distinct
    /// from the warning entry's ...d0 so a bundle carrying both still satisfies bdl-7 uniqueness.
    /// At most one error entry is written per serialization, so a constant is safe.
    /// </summary>
    private const string ErrorEntryFullUrl = "urn:uuid:00000000-0000-0000-0000-0000000000e0";

    /// <summary>
    /// Serializes a search result bundle with count-as-render pagination pattern.
    /// Streams entries from result set, counting as rendering, and generates pagination links at the end.
    /// Uses zero-copy serialization with SearchEntryResult (raw bytes from repository).
    /// </summary>
    /// <param name="outputStream">The stream to write JSON to.</param>
    /// <param name="bundleType">The FHIR bundle type (e.g., "searchset").</param>
    /// <param name="total">Total number of matching resources (optional, only when _total requested).</param>
    /// <param name="entries">Async stream of search entry results (pageSize + 1 items).</param>
    /// <param name="searchOptions">Search options containing page size and continuation token.</param>
    /// <param name="baseUrl">Base URL for generating self and next links.</param>
    /// <param name="queryString">Original query string for link generation.</param>
    /// <param name="schemaProvider">Optional FHIR schema provider for element filtering (used by _elements parameter).</param>
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <param name="flushThresholdBytes">Flush the writer when its pending buffer exceeds this size (bytes). Prevents unbounded memory growth without flushing on every entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pagination result with hasMore flag and continuation token.</returns>
    public static async Task SerializeWithPaginationAsync(
        Stream outputStream,
        string bundleType,
        int? total,
        IAsyncEnumerable<SearchEntryResult> entries,
        SearchOptions searchOptions,
        string baseUrl,
        string queryString,
        ISchema? schemaProvider = null,
        bool pretty = false,
        int flushThresholdBytes = FlushThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(outputStream, nameof(outputStream));
        EnsureArg.IsNotNullOrEmpty(bundleType, nameof(bundleType));
        EnsureArg.IsNotNull(entries, nameof(entries));
        EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

        var entryBuffer = new ArrayBufferWriter<byte>();
        await using FhirJsonWriter writer = FhirJsonWriter.Create(outputStream, pretty);
        await using FhirJsonWriter entryWriter = FhirJsonWriter.Create(entryBuffer, pretty);

        int pageSize = searchOptions.MaxItemCount;
        int entryCount = 0;
        bool hasMore = false;
        int currentOffset = 0;
        var fhirVersion = schemaProvider != null ? (FhirVersion)schemaProvider.Version : FhirVersion.R4;

        int? includesMaxCount = searchOptions.IncludesMaxItemCount;
        int includesCount = 0;
        int includesOffset = 0;
        bool hasMoreIncludes = false;

        string selfLink = string.Empty;
        string? nextLink = null;
        string? relatedLink = null;

        try
        {
            // Validated here, before any writing, so an empty Severity/Code fails inside the guard on
            // every FHIR version. Without this, an R5 tenant hits the identical throw in the unguarded
            // WriteBundleIssues call after the entry array closes (design §6/§9), resurrecting the
            // dispose-flush truncation bug.
            ValidateBundleIssues(searchOptions.BundleIssues);

            if (!string.IsNullOrWhiteSpace(searchOptions.IncludesContinuationToken)
                && IncludesContinuationToken.TryDecode(searchOptions.IncludesContinuationToken, out int includesTokenOffset, out _))
            {
                includesOffset = includesTokenOffset;
            }

            if (!string.IsNullOrWhiteSpace(searchOptions.ContinuationToken)
                && ContinuationToken.TryDecode(searchOptions.ContinuationToken, out int tokenOffset, out _))
            {
                currentOffset = tokenOffset;
            }

            WriteBundleHeader(writer, bundleType, total);

            string filteredQueryString = FilterUnsupportedParams(queryString, searchOptions.UnsupportedParams);

            selfLink = $"{baseUrl}{filteredQueryString}";

            writer.WriteStartArray("entry");

            WriteBundleIssuesPreR5(writer, searchOptions.BundleIssues, fhirVersion);

            await foreach (SearchEntryResult resource in entries.WithCancellation(cancellationToken))
            {
                if (resource.IsPagingProbe)
                {
                    // The data layer's lookahead row could not be turned into content (a corrupt
                    // payload, or a concurrent-delete miss), but its presence still proves a further
                    // page exists -- counting rendered deliveries alone cannot see that, since the row
                    // that would have crossed pageSize never arrived. Pure signal: never rendered,
                    // never counted.
                    hasMore = true;
                    continue;
                }

                if (resource.SearchMode == SearchEntryMode.Match)
                {
                    if (entryCount >= pageSize)
                    {
                        hasMore = true;
                        continue;
                    }

                    entryCount++;
                }
                else if (resource.SearchMode == SearchEntryMode.Include)
                {
                    if (includesMaxCount.HasValue && includesCount >= includesMaxCount.Value)
                    {
                        hasMoreIncludes = true;
                        continue;
                    }

                    includesCount++;
                }

                WriteBufferedEntry(writer, entryWriter, entryBuffer, resource, searchOptions, schemaProvider);

                // Flush to the HTTP response stream once the buffer exceeds the threshold.
                // This keeps memory bounded for large result sets while avoiding the overhead
                // of flushing (a syscall + potential TCP segment) on every single entry.
                if (writer.UnderlyingWriter.BytesPending >= flushThresholdBytes)
                {
                    await writer.FlushAsync(cancellationToken);
                }
            }

            // Link building parses baseUrl and can throw, so it is computed while the entry array is
            // still open - an error entry cannot be appended once the array has been closed.
            nextLink = BuildNextLink(hasMore, currentOffset, pageSize, filteredQueryString, baseUrl);
            relatedLink = BuildRelatedLink(searchOptions, includesMaxCount, includesOffset, includesCount, hasMoreIncludes, filteredQueryString, baseUrl);
        }
        catch (OperationCanceledException)
        {
            if (writer.UnderlyingWriter.BytesCommitted == 0)
            {
                DiscardTierOneBuffer(writer);
                throw;
            }

            await CloseErrorBundleAsync(writer, selfLink, searchOptions.BundleIssues, fhirVersion);
            throw;
        }
        catch (Exception ex)
        {
            if (writer.UnderlyingWriter.BytesCommitted == 0)
            {
                DiscardTierOneBuffer(writer);
                throw;
            }

            WriteOperationOutcomeEntry(
                writer,
                new IssueComponent("fatal", "exception", Diagnostics: $"Bundle serialization failed: {ex.Message}"),
                bundleType,
                fhirVersion,
                ErrorEntryFullUrl,
                selfLink);

            await CloseErrorBundleAsync(writer, selfLink, searchOptions.BundleIssues, fhirVersion);
            throw;
        }

        writer.WriteEndArray();

        WriteBundleIssues(writer, searchOptions.BundleIssues, fhirVersion);

        WriteBundleLinksFromStrings(writer, selfLink, nextLink, relatedLink);

        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Writes one complete entry into the scratch writer, then copies the finished bytes into the
    /// response writer as a single raw array element.
    /// Staging is load-bearing rather than tidy: it keeps the response writer between complete entries
    /// at all times, so a mid-entry failure dirties only the scratch buffer and the bundle stays closable.
    /// </summary>
    private static void WriteBufferedEntry(
        FhirJsonWriter writer,
        FhirJsonWriter entryWriter,
        ArrayBufferWriter<byte> entryBuffer,
        SearchEntryResult resource,
        SearchOptions searchOptions,
        ISchema? schemaProvider)
    {
        entryWriter.WriteStartObject();

        string fullUrl = $"{resource.ResourceType}/{resource.ResourceId}";
        entryWriter.WriteString("fullUrl", fullUrl);

        WriteResourceBytes(entryWriter, resource, searchOptions, schemaProvider);

#pragma warning disable CA1308
        entryWriter.WriteObject("search", w => w
            .WriteString("mode", resource.SearchMode.ToString().ToLowerInvariant()));
#pragma warning restore CA1308

        entryWriter.WriteEndObject();

        // The scratch buffer holds nothing until the writer is flushed into it.
        entryWriter.UnderlyingWriter.Flush();
        writer.UnderlyingWriter.WriteRawValue(entryBuffer.WrittenSpan, skipInputValidation: true);
        entryBuffer.Clear();
        entryWriter.UnderlyingWriter.Reset(entryBuffer);
    }

    /// <summary>
    /// Builds the <c>next</c> pagination link, or null when there is no further page.
    /// </summary>
    private static string? BuildNextLink(bool hasMore, int currentOffset, int pageSize, string filteredQueryString, string baseUrl)
    {
        if (!hasMore)
        {
            return null;
        }

        string continuationToken = ContinuationToken.Encode(currentOffset + pageSize, pageSize);
        if (string.IsNullOrWhiteSpace(continuationToken))
        {
            return null;
        }

        var parsedQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(filteredQueryString);
        parsedQuery["after"] = continuationToken;
        return $"{baseUrl}?{string.Join("&", parsedQuery.SelectMany(kvp => kvp.Value.Select(v => $"{kvp.Key}={Uri.EscapeDataString(v ?? string.Empty)}")))}";
    }

    /// <summary>
    /// Builds the <c>related</c> link pointing at the $includes continuation, or null when no
    /// included resources remain.
    /// </summary>
    private static string? BuildRelatedLink(
        SearchOptions searchOptions,
        int? includesMaxCount,
        int includesOffset,
        int includesCount,
        bool hasMoreIncludes,
        string filteredQueryString,
        string baseUrl)
    {
        if (!hasMoreIncludes || !includesMaxCount.HasValue || searchOptions.ResourceType is null)
        {
            return null;
        }

        int nextIncludesOffset = includesOffset + includesCount;
        string includesContinuationToken = IncludesContinuationToken.Encode(nextIncludesOffset, includesMaxCount.Value);

        string includesBaseUrl;
        if (baseUrl.Contains("/$includes", StringComparison.Ordinal))
        {
            includesBaseUrl = baseUrl;
        }
        else
        {
            var uri = new Uri(baseUrl, UriKind.Absolute);
            string pathWithOperation = uri.AbsolutePath.TrimEnd('/') + "/$includes";
            includesBaseUrl = $"{uri.Scheme}://{uri.Authority}{pathWithOperation}";
        }

        var parsedQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(filteredQueryString);
        parsedQuery["_includesContinuationToken"] = includesContinuationToken;
        return $"{includesBaseUrl}?{string.Join("&", parsedQuery.SelectMany(kvp => kvp.Value.Select(v => $"{kvp.Key}={Uri.EscapeDataString(v ?? string.Empty)}")))}";
    }

    /// <summary>
    /// Completes a bundle whose response has already started, then leaves the caller to rethrow.
    /// Mirrors the normal footer's <see cref="WriteBundleIssues"/> call so R5+ tenants still see
    /// their warning issues on a tier-2 failure. Only the self link is emitted: next and related
    /// describe pages this request never produced.
    /// The flush deliberately uses <see cref="CancellationToken.None"/> - an already-canceled token
    /// makes FlushAsync throw immediately, which would defeat the body completion.
    /// </summary>
    private static async Task CloseErrorBundleAsync(
        FhirJsonWriter writer,
        string selfLink,
        IReadOnlyList<IssueComponent>? bundleIssues,
        FhirVersion fhirVersion)
    {
        writer.WriteEndArray();

        WriteBundleIssues(writer, bundleIssues, fhirVersion);

        WriteBundleLinksFromStrings(writer, selfLink, nextLink: null);

        writer.WriteEndObject();
        await writer.FlushAsync(CancellationToken.None);
    }

    /// <summary>
    /// Serializes a bundle with custom pagination links (for history bundles).
    /// Uses zero-copy serialization with SearchEntryResult (raw bytes from repository).
    /// </summary>
    /// <param name="outputStream">The stream to write JSON to.</param>
    /// <param name="bundleType">The FHIR bundle type (e.g., "history").</param>
    /// <param name="total">Total number of matching resources (optional).</param>
    /// <param name="entries">Async stream of search entry results (raw bytes) to include in the bundle.</param>
    /// <param name="links">Pagination links (self, first, prev, next, last).</param>
    /// <param name="schemaProvider">Optional FHIR schema provider; supplies the version that shapes version-sensitive output.</param>
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SerializeHistoryAsync(Stream outputStream,
        string bundleType,
        int? total,
        IAsyncEnumerable<SearchEntryResult> entries,
        IReadOnlyList<FhirBundleLink>? links = null,
        ISchema? schemaProvider = null,
        bool pretty = false,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(outputStream, nameof(outputStream));
        EnsureArg.IsNotNullOrEmpty(bundleType, nameof(bundleType));
        EnsureArg.IsNotNull(entries, nameof(entries));

        int entryCount = 0;
        bool hasMore = false;
        var fhirVersion = schemaProvider != null ? (FhirVersion)schemaProvider.Version : FhirVersion.R4;
        List<FhirBundleLink>? resolvedLinks = null;

        // Resolved before any writer exists: unlike the pagination path, the error entry's url comes
        // from a caller-supplied parameter, so a throw here must not be able to land mid-bundle.
        string selfUrl = ResolveHistorySelfUrl(links);

        var entryBuffer = new ArrayBufferWriter<byte>();
        await using FhirJsonWriter writer = FhirJsonWriter.Create(outputStream, pretty);
        await using FhirJsonWriter entryWriter = FhirJsonWriter.Create(entryBuffer, pretty);

        try
        {
            WriteBundleHeader(writer, bundleType, total);

            writer.WriteStartArray("entry");

            await foreach (SearchEntryResult resource in entries.WithCancellation(cancellationToken))
            {
                if (resource.IsPagingProbe)
                {
                    // Mirrors SerializeWithPaginationAsync's identical guard: the lookahead row itself
                    // could not be turned into content, but its presence still proves a further page
                    // exists. Pure signal: never rendered, never counted toward entryCount.
                    hasMore = true;
                    continue;
                }

                entryCount++;

                if (entryCount > pageSize)
                {
                    hasMore = true;
                    continue;
                }

                WriteBufferedHistoryEntry(writer, entryWriter, entryBuffer, resource, fhirVersion);

                // Flush per entry to stream data to the client. This makes tier 2 the common case:
                // from the second entry onward the response has already started.
                await writer.FlushAsync(cancellationToken);
            }

            // GetRelationRaw() reads a possibly malformed JSON node and can throw; resolving links
            // while the entry array is still open keeps that throw inside the guard, so an error entry
            // can still be appended. Once the array is closed a throw here would resurrect the
            // dispose-flush truncation bug (design §6/§10).
            resolvedLinks = ResolveHistoryLinks(links, hasMore, entryCount);
        }
        catch (OperationCanceledException)
        {
            if (writer.UnderlyingWriter.BytesCommitted == 0)
            {
                DiscardTierOneBuffer(writer);
                throw;
            }

            await CloseHistoryErrorBundleAsync(writer);
            throw;
        }
        catch (Exception ex)
        {
            if (writer.UnderlyingWriter.BytesCommitted == 0)
            {
                DiscardTierOneBuffer(writer);
                throw;
            }

            WriteOperationOutcomeEntry(
                writer,
                new IssueComponent("fatal", "exception", Diagnostics: $"Bundle serialization failed: {ex.Message}"),
                bundleType,
                fhirVersion,
                ErrorEntryFullUrl,
                selfUrl);

            await CloseHistoryErrorBundleAsync(writer);
            throw;
        }

        writer.WriteEndArray();

        if (resolvedLinks != null)
        {
            WriteBundleLinks(writer, resolvedLinks);
        }

        writer.WriteEndObject(); // end bundle
        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Writes one complete history entry into the scratch writer, then copies the finished bytes into
    /// the response writer as a single raw array element.
    /// Staging keeps the response writer between complete entries at all times, so a mid-entry failure
    /// dirties only the scratch buffer and the bundle stays closable.
    /// </summary>
    private static void WriteBufferedHistoryEntry(
        FhirJsonWriter writer,
        FhirJsonWriter entryWriter,
        ArrayBufferWriter<byte> entryBuffer,
        SearchEntryResult resource,
        FhirVersion fhirVersion)
    {
        entryWriter.WriteStartObject();

        string fullUrl = $"{resource.ResourceType}/{resource.ResourceId}";
        if (!string.IsNullOrEmpty(resource.VersionId))
        {
            fullUrl = $"{fullUrl}/_history/{resource.VersionId}";
        }
        entryWriter.WriteString("fullUrl", fullUrl);

        WriteResourceBytes(entryWriter, resource);

        entryWriter.WriteObject("request", w => w
            .WriteString("method", resource.Request?.Method ?? "PUT")
            .WriteString("url", $"{resource.ResourceType}/{resource.ResourceId}"));

        // Stu3 bdl-4 prohibits entry.response in a history bundle; R4 reversed this and R4B/R5 carry
        // the reversal forward, so the element is required from R4 on and must be suppressed for Stu3.
        // A Stu3 deleted version consequently loses lastModified: its resource stub carries no meta,
        // and a conformant Stu3 history bundle has nowhere else to put it.
        if (fhirVersion >= FhirVersion.R4)
        {
            entryWriter.WriteObject("response", w => w
                .WriteString("status", resource.IsDeleted ? "204" : "200")
                .WriteString("lastModified", resource.LastModified.ToString("o"))
                .Condition(!string.IsNullOrEmpty(resource.VersionId), w2 => w2
                    .WriteString("etag", $"W/\"{resource.VersionId}\"")));
        }

        entryWriter.WriteEndObject();

        // The scratch buffer holds nothing until the writer is flushed into it.
        entryWriter.UnderlyingWriter.Flush();
        writer.UnderlyingWriter.WriteRawValue(entryBuffer.WrittenSpan, skipInputValidation: true);
        entryBuffer.Clear();
        entryWriter.UnderlyingWriter.Reset(entryBuffer);
    }

    /// <summary>
    /// Resolves the url carried by the history error entry's <c>request</c>: the self link's url, or the
    /// literal <c>_history</c> when there is no self link or it carries no usable url.
    /// </summary>
    private static string ResolveHistorySelfUrl(IReadOnlyList<FhirBundleLink>? links)
    {
        string? selfUrl = links?
            .FirstOrDefault(link => string.Equals(link.GetRelationRaw(), "self", StringComparison.Ordinal))?.Url;

        return string.IsNullOrEmpty(selfUrl) ? "_history" : selfUrl;
    }

    /// <summary>
    /// Resolves the "next"-suppression and empty-URL filtering that the happy-path footer applies to
    /// <paramref name="links"/>, returning freshly-built links whose relation was already read via
    /// <see cref="Ignixa.Models.BundleLink.GetRelationRaw"/> here. That read can throw on a malformed
    /// relation node; calling it here, before the entry array closes, keeps the throw inside the guard
    /// (design §6/§10) instead of in the post-guard footer. The returned links carry relations set via
    /// <see cref="CreateLink"/>, so the footer's own <c>GetRelationRaw()</c> call can never throw.
    /// </summary>
    private static List<FhirBundleLink>? ResolveHistoryLinks(
        IReadOnlyList<FhirBundleLink>? links,
        bool hasMore,
        int entryCount)
    {
        if (links == null)
        {
            return null;
        }

        bool suppressNext = !hasMore || entryCount == 0;
        var resolved = new List<FhirBundleLink>();

        foreach (var link in links)
        {
            string relation = link.GetRelationRaw() ?? "self";

            if (suppressNext && relation == "next")
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(link.Url))
            {
                continue;
            }

            resolved.Add(CreateLink(relation, link.Url));
        }

        return resolved;
    }

    /// <summary>
    /// Completes a history bundle whose response has already started, then leaves the caller to rethrow.
    /// No link array is emitted: the guard exits before the link region, and no history invariant in any
    /// supported version requires links (bdl-18's self-link rule is searchset-only).
    /// The flush deliberately uses <see cref="CancellationToken.None"/> - an already-canceled token
    /// makes FlushAsync throw immediately, which would defeat the body completion.
    /// </summary>
    private static async Task CloseHistoryErrorBundleAsync(FhirJsonWriter writer)
    {
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(CancellationToken.None);
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
    /// <returns>
    /// The outcome of the stream (design doc Section 8: this entry point's caller contract forbids
    /// rethrowing once headers are sent, so this is how a failed or truncated bundle is surfaced
    /// instead of silently reporting success).
    /// </returns>
    public static async Task<StreamingBundleResult> SerializeStreamAsync(
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

        var entryBuffer = new ArrayBufferWriter<byte>();
        await using FhirJsonWriter writer = FhirJsonWriter.Create(outputStream, pretty);
        await using FhirJsonWriter entryWriter = FhirJsonWriter.Create(entryBuffer, pretty);

        // Write bundle header
        WriteBundleHeader(writer, bundleType, total);

        // Write links
        WriteBundleLinksFromStrings(writer, selfLink, nextLink);

        // Write entry array
        writer.WriteStartArray("entry");

        // Track whether the stream ended early, and why - see StreamingBundleResult.
        Exception? streamingException = null;
        bool clientDisconnected = false;

        // Stream entry responses as they become available
        // CRITICAL: Wrap in try-catch to ensure JSON is always well-formed
        // Once headers are sent, we cannot return a different HTTP status code,
        // so we must complete the JSON structure and include the error as a bundle entry.
        try
        {
            await foreach (BundleEntryResponse entryResponse in entryResponses.WithCancellation(cancellationToken))
            {
                WriteBufferedEntryResponse(writer, entryWriter, entryBuffer, entryResponse);

                // Flush periodically to stream data to client
                await writer.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            // A canceled `cancellationToken` means the client itself disconnected: nobody is
            // listening for this response, so there is nothing to gain from an error entry. Any
            // other OperationCanceledException - a linked token inside bundleProcessor, an internal
            // timeout - reaches this same catch while the caller's own token is still live, meaning
            // a connected client is about to receive a well-formed 200 with entries silently
            // missing unless this is treated like any other mid-stream failure.

            // We read the ambient token state to classify this cancellation, but we cannot establish
            // that the token actually caused this exception. A race exists: if an unrelated internal
            // cancellation throws while the caller's token happens to be canceled, we misclassify a
            // real failure as a benign disconnect. Checking ex.CancellationToken instead is strictly
            // worse—many sources leave it empty—so we accept this narrower gap as the lesser trade-off.
            clientDisconnected = cancellationToken.IsCancellationRequested;
            streamingException = ex;
        }
        catch (Exception ex)
        {
            // Capture the exception to write as an error entry
            // This ensures the JSON remains well-formed even when enumeration fails
            streamingException = ex;
        }

        // If an exception occurred during streaming, write it as an error entry - unless the client
        // disconnected, in which case there is no reader left to show it to.
        // This ensures the bundle response is valid JSON with the error visible to the client
        if (streamingException is not null && !clientDisconnected)
        {
            WriteErrorEntry(writer, streamingException);
        }

        // Write bundle footer - ALWAYS called to ensure valid JSON. An already-canceled
        // `cancellationToken` (client disconnect) makes FlushAsync throw immediately instead of
        // completing the body, so any failure closes with CancellationToken.None - the same
        // rationale documented on every tiered entry point's error-close helper.
        await WriteBundleFooterAsync(writer, streamingException is null ? cancellationToken : CancellationToken.None);

        return streamingException is null
            ? StreamingBundleResult.Success
            : new StreamingBundleResult(Succeeded: false, streamingException, clientDisconnected);
    }

    /// <summary>
    /// Writes an error entry to the bundle when streaming fails.
    /// This ensures the response JSON remains valid even when an exception occurs.
    /// </summary>
    private static void WriteErrorEntry(FhirJsonWriter writer, Exception exception)
    {
        WriteErrorEntry(writer, new IssueComponent("fatal", "exception", Diagnostics: $"Streaming serialization failed: {exception.Message}"));
    }

    /// <summary>
    /// Writes the batch-response/transaction-response error entry shape: <c>response.status = "500 Internal Server Error"</c>
    /// plus the OperationOutcome as <c>resource</c>. Shared by both the streaming-exception path and
    /// <see cref="WriteOperationOutcomeEntry"/> so the shape is defined exactly once.
    /// </summary>
    private static void WriteErrorEntry(FhirJsonWriter writer, IssueComponent issue)
    {
        writer.WriteStartObject();

        writer.WriteStartObject("response");
        writer.WriteString("status", "500 Internal Server Error");
        writer.WriteEndObject(); // end response

        WriteOperationOutcomeResource(writer, issue);

        writer.WriteEndObject(); // end entry
    }

    /// <summary>
    /// Writes one fatal OperationOutcome bundle entry into an already-open <c>entry</c> array,
    /// shaped by <paramref name="bundleType"/> and, for history bundles, <paramref name="fhirVersion"/>.
    /// Shapes are specified in the mid-stream error handling design (§3) and must not be re-derived.
    /// </summary>
    internal static void WriteOperationOutcomeEntry(
        FhirJsonWriter writer,
        IssueComponent issue,
        string bundleType,
        FhirVersion fhirVersion,
        string fullUrl,
        string selfUrl)
    {
        // Falls back to the same "_history" literal ResolveHistorySelfUrl uses: selfUrl is
        // caller-supplied (SerializeAsync always passes string.Empty, inert only because it never
        // reaches "history" today), and WriteString rejects empty values -- which would otherwise
        // throw inside the catch and replace the original exception.
        string resolvedSelfUrl = string.IsNullOrEmpty(selfUrl) ? "_history" : selfUrl;

        switch (bundleType)
        {
            case "searchset":
                writer.WriteStartObject();
                writer.WriteString("fullUrl", fullUrl);
                WriteOperationOutcomeResource(writer, issue);
                writer.WriteObject("search", w => w.WriteString("mode", "outcome"));
                writer.WriteEndObject();
                break;

            case "history" when fhirVersion >= FhirVersion.R4:
                writer.WriteStartObject();
                writer.WriteString("fullUrl", fullUrl);
                writer.WriteObject("request", w => w
                    .WriteString("method", "GET")
                    .WriteString("url", resolvedSelfUrl));
                writer.WriteObject("response", w =>
                {
                    w.WriteString("status", "500");
                    w.WriteStartObject("outcome");
                    WriteOperationOutcomeBody(w, issue);
                    w.WriteEndObject();
                });
                writer.WriteEndObject();
                break;

            case "history":
                writer.WriteStartObject();
                writer.WriteString("fullUrl", fullUrl);
                WriteOperationOutcomeResource(writer, issue);
                writer.WriteObject("request", w => w
                    .WriteString("method", "GET")
                    .WriteString("url", resolvedSelfUrl));
                writer.WriteEndObject();
                break;

            case "batch-response":
            case "transaction-response":
                WriteErrorEntry(writer, issue);
                break;

            default:
                writer.WriteStartObject();
                writer.WriteString("fullUrl", fullUrl);
                WriteOperationOutcomeResource(writer, issue);
                writer.WriteEndObject();
                break;
        }
    }

    /// <summary>
    /// Writes a <c>resource</c> property carrying an OperationOutcome for a single issue.
    /// </summary>
    private static void WriteOperationOutcomeResource(FhirJsonWriter writer, IssueComponent issue)
    {
        writer.WriteStartObject("resource");
        WriteOperationOutcomeBody(writer, issue);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the body of an OperationOutcome (resourceType and single-issue array) into the
    /// currently open object.
    /// </summary>
    private static void WriteOperationOutcomeBody(FhirJsonWriter writer, IssueComponent issue)
    {
        writer.WriteString("resourceType", "OperationOutcome");

        writer.WriteStartArray("issue");
        writer.WriteStartObject();
        writer.WriteString("severity", issue.Severity);
        writer.WriteString("code", issue.Code);

        if (!string.IsNullOrEmpty(issue.Diagnostics))
        {
            writer.WriteString("diagnostics", issue.Diagnostics);
        }

        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes one complete entry response into the scratch writer, then copies the finished bytes into
    /// the response writer as a single raw array element.
    /// Staging keeps the response writer between complete entries at all times, so a mid-entry failure
    /// in <see cref="WriteEntryResponse"/> - notably its validating <see cref="FhirJsonWriter.WriteRawProperty"/>
    /// call on corrupt <see cref="BundleEntryResponse.ResourceJson"/> - dirties only the scratch buffer,
    /// leaving the main writer closable.
    /// </summary>
    private static void WriteBufferedEntryResponse(
        FhirJsonWriter writer,
        FhirJsonWriter entryWriter,
        ArrayBufferWriter<byte> entryBuffer,
        BundleEntryResponse response)
    {
        WriteEntryResponse(entryWriter, response);

        // The scratch buffer holds nothing until the writer is flushed into it.
        entryWriter.UnderlyingWriter.Flush();
        writer.UnderlyingWriter.WriteRawValue(entryBuffer.WrittenSpan, skipInputValidation: true);
        entryBuffer.Clear();
        entryWriter.UnderlyingWriter.Reset(entryBuffer);
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

    // Helper methods for reducing duplication

    /// <summary>
    /// Writes the bundle header (resourceType, type, total).
    /// </summary>
    private static void WriteBundleHeader(FhirJsonWriter writer, string bundleType, int? total)
    {
        writer
            .WriteStartObject()
            .WriteString("resourceType", "Bundle")
            .WriteString("type", bundleType);

        // Only write total if present (null when _total parameter not used)
        if (total.HasValue)
        {
            writer.WriteNumber("total", total.Value);
        }
    }

    /// <summary>
    /// Writes bundle links from a list of FhirBundleLink.
    /// </summary>
    private static void WriteBundleLinks(FhirJsonWriter writer, IReadOnlyList<FhirBundleLink>? links)
    {
        if (links is null || links.Count == 0)
        {
            return;
        }

        var linksWithUrl = links.Where(link => !string.IsNullOrWhiteSpace(link.Url)).ToList();
        if (linksWithUrl.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("link");

        foreach (var link in linksWithUrl)
        {
            writer.WriteStartObject();
            writer.WriteString("relation", link.GetRelationRaw() ?? "self");
            writer.WriteString("url", link.Url!);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes a string array property, skipping null, empty, or whitespace-only values.
    /// Skips emitting the array entirely if nothing survives the filter.
    /// </summary>
    private static void WriteNonEmptyStringArray(FhirJsonWriter writer, string propertyName, IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0)
        {
            return;
        }

        var nonEmptyValues = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (nonEmptyValues.Count == 0)
        {
            return;
        }

        writer.WriteStartArray(propertyName);
        foreach (var value in nonEmptyValues)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes bundle links from simple self/next string URLs.
    /// Converts to FhirBundleLink format internally.
    /// </summary>
    private static void WriteBundleLinksFromStrings(FhirJsonWriter writer, string? selfLink, string? nextLink, string? relatedLink = null)
    {
        if (string.IsNullOrEmpty(selfLink) && string.IsNullOrEmpty(nextLink) && string.IsNullOrEmpty(relatedLink))
        {
            return;
        }

        var links = new List<FhirBundleLink>();

        if (!string.IsNullOrEmpty(selfLink))
        {
            links.Add(CreateLink("self", selfLink));
        }

        if (!string.IsNullOrEmpty(nextLink))
        {
            links.Add(CreateLink("next", nextLink));
        }

        if (!string.IsNullOrEmpty(relatedLink))
        {
            links.Add(CreateLink("related", relatedLink));
        }

        WriteBundleLinks(writer, links);
    }

    private static FhirBundleLink CreateLink(string relation, string url)
    {
        var link = new FhirBundleLink { Url = url };
        link.SetRelationRaw(relation);
        return link;
    }

    /// <summary>
    /// Writes a resource, optionally filtering to specific elements.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="resource">The resource entry with raw bytes.</param>
    /// <param name="searchOptions">Optional search options (may contain Elements filter).</param>
    /// <param name="schemaProvider">Optional schema provider for element filtering.</param>
    private static void WriteResourceBytes(
        FhirJsonWriter writer,
        SearchEntryResult resource,
        SearchOptions? searchOptions = null,
        ISchema? schemaProvider = null)
    {
        if (resource.ResourceBytes.Length == 0)
        {
            // Minimal fallback (should not happen - all SearchEntryResults should have bytes)
            writer.WriteObject("resource",
                w => w.WriteString("resourceType", resource.ResourceType)
                    .WriteString("id", resource.ResourceId));
            return;
        }

        // Check if element filtering is requested
        if (searchOptions?.Elements?.Count > 0 && schemaProvider != null)
        {
            // Write filtered resource directly to the writer (no intermediate buffering)
            ResourceElementsSerializer.WriteFilteredResourceProperty(
                writer,
                "resource",
                resource.ResourceBytes,
                schemaProvider,
                searchOptions.Elements,
                resource.ResourceType);
        }
        else
        {
            // Zero-copy fast path: write raw bytes directly
            writer.WriteRawProperty("resource", resource.ResourceBytes);
        }
    }

    /// <summary>
    /// Validates that every issue's Severity/Code is present. Both are written through
    /// <see cref="FhirJsonWriter.WriteString"/>, which rejects empty values; calling this before any
    /// writing keeps that throw inside the guarded region (recoverable) instead of letting it surface
    /// from <see cref="WriteBundleIssues"/>, which runs after the guard closes on R5 tenants.
    /// </summary>
    private static void ValidateBundleIssues(IReadOnlyList<IssueComponent>? issues)
    {
        if (issues == null)
        {
            return;
        }

        foreach (var issue in issues)
        {
            EnsureArg.IsNotNullOrWhiteSpace(issue.Severity, nameof(issue.Severity));
            EnsureArg.IsNotNullOrWhiteSpace(issue.Code, nameof(issue.Code));
        }
    }

    /// <summary>
    /// Writes Bundle.issues as a complete OperationOutcome resource.
    /// Per FHIR spec, Bundle.issues is an OperationOutcome resource (not just an array).
    /// https://build.fhir.org/bundle.html
    /// </summary>
    private static void WriteBundleIssues(
        FhirJsonWriter writer,
        IReadOnlyList<IssueComponent>? issues,
        FhirVersion version)
    {
        if (issues == null || issues.Count == 0)
        {
            return;
        }

        // Bundle.issues element only exists in FHIR R5+ (not in R4/R4B/Stu3)
        if (version < FhirVersion.R5)
        {
            return;
        }

        // Write "issues" property containing an OperationOutcome resource (R5+ only)
        writer.WriteStartObject("issues");
        writer.WriteString("resourceType", "OperationOutcome");

        // Write the issue array inside the OperationOutcome
        writer.WriteStartArray("issue");

        foreach (var issue in issues)
        {
            writer.WriteStartObject();
            writer.WriteString("severity", issue.Severity);
            writer.WriteString("code", issue.Code);

            // Write details CodeableConcept as complete JSON object
            if (issue.Details != null)
            {
                WriteCodeableConcept(writer, "details", issue.Details);
            }

            if (!string.IsNullOrEmpty(issue.Diagnostics))
            {
                writer.WriteString("diagnostics", issue.Diagnostics);
            }

            WriteNonEmptyStringArray(writer, "location", issue.Location);
            WriteNonEmptyStringArray(writer, "expression", issue.Expression);

            writer.WriteEndObject();
        }

        writer.WriteEndArray(); // end issue array
        writer.WriteEndObject(); // end OperationOutcome resource
    }

    /// <summary>
    /// Writes Bundle issues as a Bundle entry with search mode="outcome".
    /// Used for FHIR R4/R4B/Stu3 which don't support Bundle.issues element.
    /// The issues are represented as an OperationOutcome resource in a Bundle entry.
    /// </summary>
    private static void WriteBundleIssuesPreR5(
        FhirJsonWriter writer,
        IReadOnlyList<IssueComponent>? issues,
        FhirVersion version)
    {
        // Only write for pre-R5 versions (R4/R4B/Stu3)
        if (version >= FhirVersion.R5)
        {
            return;
        }

        if (issues == null || issues.Count == 0)
        {
            return;
        }

        // Start a new Bundle entry
        writer.WriteStartObject();

        // Full URL: Use a synthetic URL for the outcome entry
        writer.WriteString("fullUrl", "urn:uuid:00000000-0000-0000-0000-0000000000d0");

        // Resource: OperationOutcome
        writer.WriteStartObject("resource");
        writer.WriteString("resourceType", "OperationOutcome");

        // Issue array
        writer.WriteStartArray("issue");
        foreach (var issue in issues)
        {
            writer.WriteStartObject();
            writer.WriteString("severity", issue.Severity);
            writer.WriteString("code", issue.Code);

            if (!string.IsNullOrEmpty(issue.Diagnostics))
            {
                writer.WriteString("diagnostics", issue.Diagnostics);
            }

            WriteNonEmptyStringArray(writer, "location", issue.Location);
            WriteNonEmptyStringArray(writer, "expression", issue.Expression);

            writer.WriteEndObject();
        }

        writer.WriteEndArray(); // end issue array
        writer.WriteEndObject(); // end OperationOutcome resource

        // Search: Mark as outcome entry
        writer.WriteObject("search", w => w.WriteString("mode", "outcome"));

        writer.WriteEndObject(); // end entry
    }

    /// <summary>
    /// Writes a complete CodeableConcept JSON object with all FHIR properties.
    /// </summary>
    private static void WriteCodeableConcept(
        FhirJsonWriter writer,
        string propertyName,
        Ignixa.Models.CodeableConcept concept)
    {
        writer.WriteStartObject(propertyName);

        // Write coding array if present
        if (concept.Coding != null && concept.Coding.Count > 0)
        {
            writer.WriteStartArray("coding");
            foreach (var coding in concept.Coding)
            {
                writer.WriteStartObject();

                if (!string.IsNullOrEmpty(coding.System))
                {
                    writer.WriteString("system", coding.System);
                }

                if (!string.IsNullOrEmpty(coding.Version))
                {
                    writer.WriteString("version", coding.Version);
                }

                if (!string.IsNullOrEmpty(coding.Code))
                {
                    writer.WriteString("code", coding.Code);
                }

                if (!string.IsNullOrEmpty(coding.Display))
                {
                    writer.WriteString("display", coding.Display);
                }

                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        // Write text if present
        if (!string.IsNullOrEmpty(concept.Text))
        {
            writer.WriteString("text", concept.Text);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Filters unsupported parameters from the query string.
    /// </summary>
    /// <param name="queryString">Original query string (may start with '?').</param>
    /// <param name="unsupportedParams">List of parameter names to filter out.</param>
    /// <returns>Filtered query string (with leading '?' if original had it).</returns>
    private static string FilterUnsupportedParams(string queryString, IReadOnlyList<string> unsupportedParams)
    {
        if (string.IsNullOrWhiteSpace(queryString) || unsupportedParams == null || unsupportedParams.Count == 0)
        {
            return queryString ?? string.Empty;
        }

        // Remove leading '?' if present, we'll add it back later
        bool hasLeadingQuestionMark = queryString.StartsWith('?');
        string queryWithoutPrefix = hasLeadingQuestionMark ? queryString[1..] : queryString;

        if (string.IsNullOrWhiteSpace(queryWithoutPrefix))
        {
            return queryString;
        }

        // Parse query string
        var parsedQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryWithoutPrefix);

        // Build set of unsupported parameter keys (including special handling for _sort=fieldName)
        var unsupportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unsupportedSortFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var unsupported in unsupportedParams)
        {
            if (unsupported.StartsWith("_sort=", StringComparison.OrdinalIgnoreCase))
            {
                // Track unsupported sort fields (e.g., "invalidField" from "_sort=invalidField")
                string fieldName = unsupported.Substring("_sort=".Length);
                unsupportedSortFields.Add(fieldName);
            }
            else
            {
                // Regular unsupported parameter key (e.g., "_sort" or "name")
                unsupportedKeys.Add(unsupported);
            }
        }

        // If any sort field is unsupported, the entire _sort parameter is unsupported
        if (unsupportedSortFields.Count > 0)
        {
            unsupportedKeys.Add("_sort");
        }

        // Filter out unsupported parameters
        var filteredQuery = parsedQuery
            .Where(kvp => !unsupportedKeys.Contains(kvp.Key))
            .SelectMany(kvp => kvp.Value.Select(v => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(v ?? string.Empty)}"));

        string result = string.Join("&", filteredQuery);

        // Add back leading '?' if original had it and result is not empty
        if (hasLeadingQuestionMark && !string.IsNullOrEmpty(result))
        {
            result = "?" + result;
        }

        return result;
    }

    /// <summary>
    /// Writes the bundle footer (end entry array, end bundle object, flush).
    /// </summary>
    private static async Task WriteBundleFooterAsync(FhirJsonWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteEndArray(); // end entry array
        writer.WriteEndObject(); // end bundle

        await writer.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// Result of pagination operation containing hasMore flag and continuation token.
/// </summary>
/// <param name="HasMore">Indicates if there are more results beyond the current page.</param>
/// <param name="ContinuationToken">Token for fetching the next page of results.</param>
/// <param name="RenderedCount">Number of entries actually rendered (should be pageSize or less).</param>
public record PaginationResult(bool HasMore, string? ContinuationToken, int RenderedCount);

/// <summary>
/// Outcome of <see cref="StreamingBundleSerializer.SerializeStreamAsync"/>. The batch/transaction
/// response is already committed to the client by the time this returns, so nothing here can change
/// the HTTP status - it exists purely so the caller can log what actually happened instead of
/// unconditionally reporting success.
/// </summary>
/// <param name="Succeeded">True when every entry response streamed with no exception or cancellation.</param>
/// <param name="Exception">The exception (including <see cref="OperationCanceledException"/>) that ended the stream early, or null on success.</param>
/// <param name="ClientDisconnected">
/// True when <paramref name="Exception"/> is cancellation raised because the caller's own
/// <c>cancellationToken</c> was signaled - i.e. nobody is listening for the response any more, so a
/// quiet log (if any) is appropriate. False for every other failure, including cancellation raised by
/// some other source (a linked token inside the entry producer, an internal timeout): that truncates
/// a live, connected client's response and warrants a normal error log.
/// </param>
public sealed record StreamingBundleResult(bool Succeeded, Exception? Exception, bool ClientDisconnected)
{
    /// <summary>The result for a bundle that streamed every entry successfully.</summary>
    public static readonly StreamingBundleResult Success = new(true, null, false);
}
