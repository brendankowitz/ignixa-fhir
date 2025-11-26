// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using EnsureThat;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IO;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Search.Infrastructure;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Executes a single bundle entry by routing through ASP.NET Core pipeline using mini HttpContext objects.
/// This enables bundle entries to automatically access all FHIR endpoints without manual switch-statement routing.
/// Creates isolated IFhirRequestContext per entry for thread-safe concurrent processing.
/// </summary>
public class BundleEntryExecutor
{
    private readonly IPipelineExecutor _pipelineExecutor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly ILogger<BundleEntryExecutor> _logger;

    public BundleEntryExecutor(
        IPipelineExecutor pipelineExecutor,
        IHttpContextAccessor httpContextAccessor,
        IFhirRequestContextAccessor fhirContextAccessor,
        RecyclableMemoryStreamManager memoryStreamManager,
        ILogger<BundleEntryExecutor> logger)
    {
        _pipelineExecutor = EnsureArg.IsNotNull(pipelineExecutor, nameof(pipelineExecutor));
        _httpContextAccessor = EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
        _fhirContextAccessor = EnsureArg.IsNotNull(fhirContextAccessor, nameof(fhirContextAccessor));
        _memoryStreamManager = EnsureArg.IsNotNull(memoryStreamManager, nameof(memoryStreamManager));
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
    }

    /// <summary>
    /// Executes a single bundle entry by routing through ASP.NET Core pipeline.
    /// </summary>
    /// <param name="entry">The bundle entry to execute.</param>
    /// <param name="referenceContext">Reference resolution context for urn:uuid references.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="deferredWriteCoordinator">Optional coordinator for deferred batch writes (Phase 1.1a).</param>
    /// <returns>Bundle entry response with status, location, etag.</returns>
    public async Task<BundleEntryResponse> ExecuteAsync(
        BundleEntryContext entry,
        ReferenceResolutionContext referenceContext,
        CancellationToken cancellationToken,
        DeferredWriteCoordinator? deferredWriteCoordinator = null)
    {
        EnsureArg.IsNotNull(entry, nameof(entry));
        EnsureArg.IsNotNull(referenceContext, nameof(referenceContext));

        _logger.LogInformation(
            "Executing bundle entry {Index}: {Verb} {Url}",
            entry.Index,
            entry.HttpVerb,
            entry.RequestUrl);

        try
        {
            // Get parent FHIR request context and clone it for this bundle entry
            // This creates an isolated context for thread-safe concurrent processing
            var parentContext = _fhirContextAccessor.RequestContext
                ?? throw new InvalidOperationException("No parent FHIR request context available for bundle entry execution");

            // Create ISOLATED child context for this bundle entry
            // AsyncLocal ensures concurrent entries don't interfere with each other
            var entryContext = new FhirRequestContext
            {
                // ===== Inherited from Parent =====
                TenantId = parentContext.TenantId,
                TenantConfiguration = parentContext.TenantConfiguration,
                FhirVersion = parentContext.FhirVersion,
                VersionContext = parentContext.VersionContext,
                DeferredWriteCoordinator = deferredWriteCoordinator ?? parentContext.DeferredWriteCoordinator,

                // ===== Bundle-Specific (Shared) =====
                ExecutingBatchOrTransaction = true,
                IsBackgroundTask = parentContext.IsBackgroundTask,

                // ===== Entry-Specific (Unique Per Entry) =====
                BundleEntryIndex = entry.Index,
                ResourceType = ExtractResourceTypeFromUrl(entry.RequestUrl),
                BundleAssignedResourceId = entry.AssignedResourceId

                // Note: BundleIssues and Properties are read-only get-only properties
                // They are auto-initialized in FhirRequestContext constructor
                // We'll copy parent properties after initialization
            };

            // Copy parent Properties to child context (shallow copy for isolation)
            foreach (var kvp in parentContext.Properties)
            {
                entryContext.Properties[kvp.Key] = kvp.Value;
            }

            // Set isolated context for this async execution context (AsyncLocal)
            _fhirContextAccessor.RequestContext = entryContext;

            _logger.LogDebug(
                "Created isolated context for bundle entry {EntryIndex}: ResourceType={ResourceType}, TenantId={TenantId}",
                entry.Index,
                entryContext.ResourceType,
                entryContext.TenantId);

            // Create mini HttpContext for bundle entry
            var httpContext = new DefaultHttpContext();
            var responseBodyStream = _memoryStreamManager.GetStream("bundle-entry-response");

            // Copy RequestServices from parent HttpContext if available
            // This allows endpoints to resolve dependencies via [FromServices]
            if (_httpContextAccessor.HttpContext?.RequestServices != null)
            {
                httpContext.RequestServices = _httpContextAccessor.HttpContext.RequestServices;

                // NOTE: Tenant context propagation now handled by IFhirRequestContext (AsyncLocal storage)
                // No need to copy HttpContext.Items - the isolated context is already set above (lines 76-106)
            }

            // Set request properties
            httpContext.Request.Protocol = "HTTP/1.1";
            httpContext.Request.Scheme = "http";
            httpContext.Request.Method = entry.HttpVerb.ToUpperInvariant();
            httpContext.Request.PathBase = string.Empty;
            httpContext.Request.Path = ParsePath(entry.RequestUrl).Value ?? string.Empty;
            httpContext.Request.QueryString = ParseQueryString(entry.RequestUrl);
            httpContext.Request.Body = Stream.Null;

            // Set response body to MemoryStream so we can capture output
            httpContext.Response.Body = responseBodyStream;

            // Add conditional operation headers from bundle entry
            if (!string.IsNullOrWhiteSpace(entry.IfNoneExist))
            {
                httpContext.Request.Headers["If-None-Exist"] = entry.IfNoneExist;
                _logger.LogDebug(
                    "Added If-None-Exist header to entry {Index}: {IfNoneExist}",
                    entry.Index,
                    entry.IfNoneExist);
            }

            if (!string.IsNullOrWhiteSpace(entry.IfMatch))
            {
                httpContext.Request.Headers["If-Match"] = entry.IfMatch;
                _logger.LogDebug(
                    "Added If-Match header to entry {Index}: {IfMatch}",
                    entry.Index,
                    entry.IfMatch);
            }

            // Serialize resource to request body (if present)
            if (entry.Resource != null)
            {
                httpContext.Request.Body = SerializeResourceToStream(entry);
                httpContext.Request.ContentType = "application/fhir+json";
            }

            // NOTE: Bundle processing context is now fully managed by IFhirRequestContext (AsyncLocal)
            // No need to set HttpContext.Items - all context is already set in entryContext above (lines 83-104)
            // - DeferredWriteCoordinator: Set in entryContext.DeferredWriteCoordinator
            // - BundleAssignedResourceId: Set in entryContext.BundleAssignedResourceId
            // - BundleEntryIndex: Set in entryContext.BundleEntryIndex

            // Execute through ASP.NET Core pipeline
            // This automatically routes to correct endpoint handler (FhirEndpoints)
            await _pipelineExecutor.ExecuteAsync(httpContext);

            // Extract response from HttpContext
            return await ExtractResponseAsync(httpContext, cancellationToken);
        }
        catch (FhirException fhirEx)
        {
            // FHIR exceptions have proper HTTP status codes and OperationOutcomes
            // Examples: ValidationException (400), etc.
            _logger.LogWarning(
                "FHIR error in bundle entry {Index} {Verb} {Url}: {Message}",
                entry.Index,
                entry.HttpVerb,
                entry.RequestUrl,
                fhirEx.Message);

            var operationOutcome = fhirEx.OperationOutcome;
            var resourceJson = operationOutcome.SerializeToString();

            return new BundleEntryResponse
            {
                StatusCode = fhirEx.StatusCode,
                Status = $"{fhirEx.StatusCode} {GetReasonPhrase(fhirEx.StatusCode)}",
                Location = null,
                ETag = null,
                ResourceJson = resourceJson,
                LastModified = null
            };
        }
        catch (Exception ex)
        {
            // Non-FHIR exceptions: Create OperationOutcome with detailed error information
            _logger.LogError(
                ex,
                "Unexpected error executing bundle entry {Index}: {Verb} {Url}",
                entry.Index,
                entry.HttpVerb,
                entry.RequestUrl);

            // Determine if this is a conflict (optimistic concurrency) or internal error
            var isConflict = ex.Message.Contains("recently updated", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("constraint violation", StringComparison.OrdinalIgnoreCase);

            var statusCode = isConflict ? 409 : 500;
            var severity = isConflict ? OperationOutcomeJsonNode.IssueSeverity.Error : OperationOutcomeJsonNode.IssueSeverity.Fatal;
            var code = isConflict ? OperationOutcomeJsonNode.IssueType.Conflict : OperationOutcomeJsonNode.IssueType.Exception;

            // Create OperationOutcome with detailed diagnostics
            var operationOutcome = new OperationOutcomeJsonNode();
            var issueList = new List<OperationOutcomeJsonNode.IssueComponent>
            {
                new OperationOutcomeJsonNode.IssueComponent()
                {
                    Severity = severity,
                    Code = code,
                    Diagnostics = ex.Message
                }
            };
            foreach (var item in issueList)
            {
                operationOutcome.Issue.Add(item);
            }

            var resourceJson = operationOutcome.SerializeToString();

            return new BundleEntryResponse
            {
                StatusCode = statusCode,
                Status = $"{statusCode} {GetReasonPhrase(statusCode)}",
                Location = null,
                ETag = null,
                ResourceJson = resourceJson,
                LastModified = null
            };
        }
    }

    // CA1861: Prefer static readonly for repeated array arguments
    private static readonly char[] UrlSeparators = { '/', '?' };

    /// <summary>
    /// Extracts resource type from bundle entry's request URL.
    /// Examples: "Patient" from "Patient/123", "Observation" from "Observation?subject=Patient/123"
    /// </summary>
    private static string? ExtractResourceTypeFromUrl(string requestUrl)
    {
        if (string.IsNullOrWhiteSpace(requestUrl))
        {
            return null;
        }

        // Remove leading slash and split by / and ?
        var url = requestUrl.TrimStart('/');
        var segments = url.Split(UrlSeparators, StringSplitOptions.RemoveEmptyEntries);

        // First segment is the resource type (e.g., "Patient/123" → "Patient")
        return segments.Length > 0 ? segments[0] : null;
    }

    /// <summary>
    /// Parses the path component from a bundle request URL.
    /// </summary>
    private static PathString ParsePath(string requestUrl)
    {
        // Handle URLs like "Patient/123", "/Patient/123", or "Patient/123?_format=json"
        var uri = new Uri("http://localhost/" + requestUrl.TrimStart('/'));
        return new PathString(uri.AbsolutePath);
    }

    /// <summary>
    /// Parses the query string component from a bundle request URL.
    /// </summary>
    private static QueryString ParseQueryString(string requestUrl)
    {
        // Handle URLs like "Patient/123?_format=json" or "Patient?name=Smith"
        var uri = new Uri("http://localhost/" + requestUrl.TrimStart('/'));
        return new QueryString(uri.Query);
    }

    /// <summary>
    /// Serializes a resource to a stream for the request body.
    /// Uses pre-captured RawJson from bundle parsing to avoid re-serialization overhead.
    /// </summary>
    private Stream SerializeResourceToStream(BundleEntryContext entry)
    {
        // Fast path: Use pre-captured raw JSON from parsing
        if (!string.IsNullOrEmpty(entry.RawJson))
        {
            var stream = _memoryStreamManager.GetStream("bundle-entry-request");
            var bytes = Encoding.UTF8.GetBytes(entry.RawJson);
            stream.Write(bytes, 0, bytes.Length);
            stream.Position = 0;
            return stream;
        }

        // Fallback: If RawJson not available, this indicates a parser bug
        _logger.LogError(
            "RawJson not available for entry {Index}. Bundle parsers must capture raw JSON during parsing.",
            entry.Index);

        throw new InvalidOperationException(
            $"RawJson not available for entry {entry.Index}. " +
            "Bundle parsers must capture raw JSON during parsing.");
    }

    /// <summary>
    /// Extracts the response from HttpContext after pipeline execution.
    /// </summary>
    private async Task<BundleEntryResponse> ExtractResponseAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = httpContext.Response;

        // Read response body
        string? resourceJson = null;
        if (response.Body != null && response.Body.CanSeek && response.Body.Length > 0)
        {
            response.Body.Position = 0;
            using var reader = new StreamReader(response.Body, Encoding.UTF8, leaveOpen: true);
            resourceJson = await reader.ReadToEndAsync(cancellationToken);
        }
        else if (response.Body == null)
        {
            _logger.LogWarning(
                "Response body is null for bundle entry, this may indicate the pipeline replaced the response stream");
        }

        // Extract headers using string-based access
        string? etag = null;
        if (response.Headers.TryGetValue("ETag", out var etagValues))
        {
            etag = etagValues.ToString();
        }

        DateTimeOffset? lastModified = null;
        if (response.Headers.TryGetValue("Last-Modified", out var lastModifiedValues))
        {
            if (DateTimeOffset.TryParse(lastModifiedValues.ToString(), out var parsedDate))
            {
                lastModified = parsedDate;
            }
        }

        string? location = null;
        if (response.Headers.TryGetValue("Location", out var locationValues))
        {
            location = locationValues.ToString();
        }

        // Get reason phrase for status code
        string status = $"{response.StatusCode} {GetReasonPhrase(response.StatusCode)}";

        return new BundleEntryResponse
        {
            StatusCode = response.StatusCode,
            Status = status,
            Location = location,
            ETag = etag,
            ResourceJson = resourceJson,
            LastModified = lastModified
        };
    }

    /// <summary>
    /// Gets the HTTP reason phrase for a status code.
    /// </summary>
    private static string GetReasonPhrase(int statusCode)
    {
        return statusCode switch
        {
            200 => "OK",
            201 => "Created",
            204 => "No Content",
            304 => "Not Modified",
            400 => "Bad Request",
            404 => "Not Found",
            409 => "Conflict",
            412 => "Precondition Failed",
            422 => "Unprocessable Entity",
            500 => "Internal Server Error",
            _ => string.Empty
        };
    }
}
