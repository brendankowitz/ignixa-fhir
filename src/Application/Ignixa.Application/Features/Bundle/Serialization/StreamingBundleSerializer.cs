// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using EnsureThat;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Abstractions;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;

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

        // Write bundle header
        WriteBundleHeader(writer, bundleType, total);

        // Write links
        WriteBundleLinksFromStrings(writer, selfLink, nextLink);

        // Write entry array
        writer.WriteStartArray("entry");

        // Stream entries as they become available (zero-copy from raw bytes)
        await foreach (SearchEntryResult resource in entries.WithCancellation(cancellationToken))
        {
            writer.WriteStartObject();

            // Write fullUrl
            string fullUrl = $"{resource.ResourceType}/{resource.ResourceId}";
            writer.WriteString("fullUrl", fullUrl);

            // Write resource using helper
            WriteResourceBytes(writer, resource);

            // Write search metadata - use resource's SearchMode (match, include, or outcome)
            // CA1308 suppressed: JSON requires lowercase values for FHIR compliance
#pragma warning disable CA1308
            writer.WriteObject("search", w => w
                .WriteString("mode", resource.SearchMode.ToString().ToLowerInvariant()));
#pragma warning restore CA1308

            writer.WriteEndObject(); // end entry

            // Flush periodically to stream data to client
            await writer.FlushAsync(cancellationToken);
        }

        // Write bundle footer
        await WriteBundleFooterAsync(writer, cancellationToken);
    }

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
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(outputStream, nameof(outputStream));
        EnsureArg.IsNotNullOrEmpty(bundleType, nameof(bundleType));
        EnsureArg.IsNotNull(entries, nameof(entries));
        EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

        await using FhirJsonWriter writer = FhirJsonWriter.Create(outputStream, pretty);

        int pageSize = searchOptions.MaxItemCount;
        int entryCount = 0;
        bool hasMore = false;
        int currentOffset = 0;
        var fhirVersion = schemaProvider != null ? (FhirSpecification)schemaProvider.Version : FhirSpecification.R4;

        // Parse current offset from existing continuation token
        if (!string.IsNullOrWhiteSpace(searchOptions.ContinuationToken))
        {
            if (ContinuationToken.TryDecode(searchOptions.ContinuationToken, out int tokenOffset, out _))
            {
                currentOffset = tokenOffset;
            }
        }

        // Write bundle header
        WriteBundleHeader(writer, bundleType, total);

        // Filter unsupported parameters from query string for self link
        string filteredQueryString = FilterUnsupportedParams(queryString, searchOptions.UnsupportedParams);

        // Build self link (always available)
        string selfLink = $"{baseUrl}{filteredQueryString}";

        // Write entry array
        writer.WriteStartArray("entry");

        // For R4/R4B/STU3, write issues as a Bundle entry with search.mode="outcome" (pre-R5 format)
        // R5+ will write Bundle.issues property at the end instead
        WriteBundleIssuesPreR5(writer, searchOptions.BundleIssues, fhirVersion);

        // Write buffered entries
        // Note: Entries stream in phases: Match results (pageSize+1), then Include, then RevInclude
        // The +1 Match result is used to detect if there are more pages (hasMore flag)
        await foreach (SearchEntryResult resource in entries.WithCancellation(cancellationToken))
        {
            // For Match entries: check if we've reached the pageSize limit
            // The database returns pageSize+1 results to detect pagination
            if (resource.SearchMode == SearchEntryMode.Match)
            {
                if (entryCount >= pageSize)
                {
                    // We've found the +1 indicator: set hasMore flag but skip rendering
                    // Continue processing Include/RevInclude results that follow
                    hasMore = true;
                    continue;
                }

                // Within limit: we'll render this Match entry
                entryCount++;
            }

            // Write entry (all Include/RevInclude entries and Match entries within pageSize)
            writer.WriteStartObject();

            // Write fullUrl
            string fullUrl = $"{resource.ResourceType}/{resource.ResourceId}";
            writer.WriteString("fullUrl", fullUrl);

            // Write resource using helper (with optional element filtering)
            WriteResourceBytes(writer, resource, searchOptions, schemaProvider);

            // Write search metadata
#pragma warning disable CA1308
            writer.WriteObject("search", w => w
                .WriteString("mode", resource.SearchMode.ToString().ToLowerInvariant()));
#pragma warning restore CA1308

            writer.WriteEndObject(); // end entry
        }

        // End entry array
        writer.WriteEndArray();

        // Write Bundle issues if present (e.g., unsupported search parameters)
        // NOTE: Bundle.issues only exists in FHIR R5+, not in R4/R4B/STU3
        WriteBundleIssues(writer, searchOptions.BundleIssues, fhirVersion);

        // Generate continuation token if there are more results
        string? continuationToken = null;
        if (hasMore)
        {
            int nextOffset = currentOffset + pageSize;
            continuationToken = ContinuationToken.Encode(nextOffset, pageSize);
        }

        // Generate next link if there are more results
        string? nextLink = null;
        if (hasMore && !string.IsNullOrWhiteSpace(continuationToken))
        {
            var parsedQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(filteredQueryString);
            parsedQuery["after"] = continuationToken;
            nextLink = $"{baseUrl}?{string.Join("&", parsedQuery.SelectMany(kvp => kvp.Value.Select(v => $"{kvp.Key}={Uri.EscapeDataString(v ?? string.Empty)}")))}";
        }

        // Write links (now that we know if there's a next link)
        WriteBundleLinksFromStrings(writer, selfLink, nextLink);

        // Write bundle footer
        writer.WriteEndObject(); // end bundle
        await writer.FlushAsync(cancellationToken);
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
    /// <param name="pretty">Whether to format JSON with indentation.</param>
    /// <param name="pageSize"></param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SerializeHistoryAsync(Stream outputStream,
        string bundleType,
        int? total,
        IAsyncEnumerable<SearchEntryResult> entries,
        IReadOnlyList<BundleLinkJsonNode>? links = null,
        bool pretty = false,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsNotNull(outputStream, nameof(outputStream));
        EnsureArg.IsNotNullOrEmpty(bundleType, nameof(bundleType));
        EnsureArg.IsNotNull(entries, nameof(entries));

        int entryCount = 0;
        bool hasMore = false;

        await using FhirJsonWriter writer = FhirJsonWriter.Create(outputStream, pretty);

        // Write bundle header
        WriteBundleHeader(writer, bundleType, total);

        // Write links
        //WriteBundleLinks(writer, links);

        // Write entry array
        writer.WriteStartArray("entry");

        // Stream entries as they become available (zero-copy from raw bytes)
        await foreach (SearchEntryResult resource in entries.WithCancellation(cancellationToken))
        {
            entryCount++;

            if (entryCount > pageSize)
            {
                hasMore = true;
                continue;
            }

            writer.WriteStartObject();

            // Write fullUrl with version for history bundles
            string fullUrl = $"{resource.ResourceType}/{resource.ResourceId}";
            if (!string.IsNullOrEmpty(resource.VersionId))
            {
                fullUrl = $"{fullUrl}/_history/{resource.VersionId}";
            }
            writer.WriteString("fullUrl", fullUrl);

            // Write resource using helper
            WriteResourceBytes(writer, resource);

            // Write request metadata for history bundles
            writer.WriteObject("request", w => w
                .WriteString("method", resource.Request?.Method ?? "PUT")
                .WriteString("url", $"{resource.ResourceType}/{resource.ResourceId}"));

            // Write response metadata for history bundles
            writer.WriteObject("response", w => w
                .WriteString("status", resource.IsDeleted ? "204" : "200")
                .WriteString("lastModified", resource.LastModified.ToString("o"))
                .Condition(!string.IsNullOrEmpty(resource.VersionId), w2 => w2
                    .WriteString("etag", $"W/\"{resource.VersionId}\"")));

            writer.WriteEndObject(); // end entry

            // Flush periodically to stream data to client
            await writer.FlushAsync(cancellationToken);
        }

        writer.WriteEndArray();

        if (links != null)
        {
            if (!hasMore || entryCount == 0)
            {
                links = links.Where(x => x.Relation != "next").ToList();
            }

            WriteBundleLinks(writer, links);
        }

        writer.WriteEndObject(); // end bundle
        await writer.FlushAsync(cancellationToken);
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

        // Write bundle header
        WriteBundleHeader(writer, bundleType, total);

        // Write links
        WriteBundleLinksFromStrings(writer, selfLink, nextLink);

        // Write entry array
        writer.WriteStartArray("entry");

        // Stream entry responses as they become available
        await foreach (BundleEntryResponse entryResponse in entryResponses.WithCancellation(cancellationToken))
        {
            WriteEntryResponse(writer, entryResponse);

            // Flush periodically to stream data to client
            await writer.FlushAsync(cancellationToken);
        }

        // Write bundle footer
        await WriteBundleFooterAsync(writer, cancellationToken);
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
    /// Writes bundle links from a list of BundleLinkJsonNode.
    /// </summary>
    private static void WriteBundleLinks(FhirJsonWriter writer, IReadOnlyList<BundleLinkJsonNode>? links)
    {
        if (links is null || links.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("link");

        foreach (var link in links)
        {
            writer.WriteStartObject();
            writer.WriteString("relation", link.Relation ?? "self");
            writer.WriteString("url", link.Url ?? string.Empty);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes bundle links from simple self/next string URLs.
    /// Converts to BundleLinkJsonNode format internally.
    /// </summary>
    private static void WriteBundleLinksFromStrings(FhirJsonWriter writer, string? selfLink, string? nextLink)
    {
        if (string.IsNullOrEmpty(selfLink) && string.IsNullOrEmpty(nextLink))
        {
            return;
        }

        var links = new List<BundleLinkJsonNode>();

        if (!string.IsNullOrEmpty(selfLink))
        {
            links.Add(new BundleLinkJsonNode(new JsonObject(), null)
            {
                Relation = "self",
                Url = selfLink
            });
        }

        if (!string.IsNullOrEmpty(nextLink))
        {
            links.Add(new BundleLinkJsonNode(new JsonObject(), null)
            {
                Relation = "next",
                Url = nextLink
            });
        }

        WriteBundleLinks(writer, links);
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
    /// Writes Bundle.issues as a complete OperationOutcome resource.
    /// Per FHIR spec, Bundle.issues is an OperationOutcome resource (not just an array).
    /// https://build.fhir.org/bundle.html
    /// </summary>
    private static void WriteBundleIssues(
        FhirJsonWriter writer,
        IReadOnlyList<IssueComponent>? issues,
        FhirSpecification version)
    {
        if (issues == null || issues.Count == 0)
        {
            return;
        }

        // Bundle.issues element only exists in FHIR R5+ (not in R4/R4B/STU3)
        if (version < FhirSpecification.R5)
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

            if (issue.Location != null && issue.Location.Count > 0)
            {
                writer.WriteStartArray("location");
                foreach (var location in issue.Location)
                {
                    writer.WriteStringValue(location);
                }
                writer.WriteEndArray();
            }

            if (issue.Expression != null && issue.Expression.Count > 0)
            {
                writer.WriteStartArray("expression");
                foreach (var expr in issue.Expression)
                {
                    writer.WriteStringValue(expr);
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray(); // end issue array
        writer.WriteEndObject(); // end OperationOutcome resource
    }

    /// <summary>
    /// Writes Bundle issues as a Bundle entry with search mode="outcome".
    /// Used for FHIR R4/R4B/STU3 which don't support Bundle.issues element.
    /// The issues are represented as an OperationOutcome resource in a Bundle entry.
    /// </summary>
    private static void WriteBundleIssuesPreR5(
        FhirJsonWriter writer,
        IReadOnlyList<IssueComponent>? issues,
        FhirSpecification version)
    {
        // Only write for pre-R5 versions (R4/R4B/STU3)
        if (version >= FhirSpecification.R5)
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
        writer.WriteString("fullUrl", "urn:uuid:operation-outcome");

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

            if (issue.Location != null && issue.Location.Count > 0)
            {
                writer.WriteStartArray("location");
                foreach (var location in issue.Location)
                {
                    writer.WriteStringValue(location);
                }
                writer.WriteEndArray();
            }

            if (issue.Expression != null && issue.Expression.Count > 0)
            {
                writer.WriteStartArray("expression");
                foreach (var expr in issue.Expression)
                {
                    writer.WriteStringValue(expr);
                }
                writer.WriteEndArray();
            }

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
        CodeableConceptJsonNode concept)
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
