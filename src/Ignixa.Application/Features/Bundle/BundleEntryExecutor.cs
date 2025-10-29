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

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Executes a single bundle entry by routing through ASP.NET Core pipeline using mini HttpContext objects.
/// This enables bundle entries to automatically access all FHIR endpoints without manual switch-statement routing.
/// </summary>
public class BundleEntryExecutor
{
    private readonly IPipelineExecutor _pipelineExecutor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly ILogger<BundleEntryExecutor> _logger;

    public BundleEntryExecutor(
        IPipelineExecutor pipelineExecutor,
        IHttpContextAccessor httpContextAccessor,
        RecyclableMemoryStreamManager memoryStreamManager,
        ILogger<BundleEntryExecutor> logger)
    {
        _pipelineExecutor = EnsureArg.IsNotNull(pipelineExecutor, nameof(pipelineExecutor));
        _httpContextAccessor = EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
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
            // Create mini HttpContext for bundle entry
            var httpContext = new DefaultHttpContext();
            var responseBodyStream = _memoryStreamManager.GetStream("bundle-entry-response");

            // Copy RequestServices from parent HttpContext if available
            // This allows endpoints to resolve dependencies via [FromServices]
            if (_httpContextAccessor.HttpContext?.RequestServices != null)
            {
                httpContext.RequestServices = _httpContextAccessor.HttpContext.RequestServices;

                // CRITICAL: Propagate tenant context from parent HttpContext to bundle entry HttpContext
                // This ensures DeferredWriteCoordinator can extract tenant context for partition routing
                // Multi-Tenancy (ADR-2523 Phase 20)
                if (_httpContextAccessor.HttpContext.Items.TryGetValue("TenantId", out var tenantId))
                {
                    httpContext.Items["TenantId"] = tenantId;
                    _logger.LogDebug(
                        "Propagated tenant {TenantId} to bundle entry {Index}",
                        tenantId,
                        entry.Index);
                }

                if (_httpContextAccessor.HttpContext.Items.TryGetValue("TenantConfiguration", out var tenantConfig))
                {
                    httpContext.Items["TenantConfiguration"] = tenantConfig;
                }

                // Propagate validation tier override from parent HttpContext (for Prefer header)
                if (_httpContextAccessor.HttpContext.Items.TryGetValue("ValidationTierOverride", out var validationOverride))
                {
                    httpContext.Items["ValidationTierOverride"] = validationOverride;
                    _logger.LogDebug(
                        "Propagated validation tier override to bundle entry {Index}: {ValidationTier}",
                        entry.Index,
                        validationOverride);
                }
            }

            try
            {
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

                // Pass coordinator via HttpContext.Items for deferred writes
                // This enables handlers to detect bundle context and queue writes appropriately
                if (deferredWriteCoordinator != null)
                {
                    httpContext.Items["DeferredWriteCoordinator"] = deferredWriteCoordinator;

                    // Use AsyncLocal instead of HttpContext.Items to avoid race conditions
                    // when processing bundle entries concurrently
                    httpContext.SetBundleEntryIndex(entry.Index);

                    _logger.LogWarning(
                        "EXECUTOR: Set entry index {EntryIndex} in AsyncLocal for {Verb} {Url}",
                        entry.Index,
                        entry.HttpVerb,
                        entry.RequestUrl);
                }

                // Pass assigned resource ID for POST operations with urn:uuid fullUrls
                // This ensures conditional creates use the pre-assigned ID for reference resolution
                if (!string.IsNullOrWhiteSpace(entry.AssignedResourceId))
                {
                    httpContext.Items["BundleAssignedResourceId"] = entry.AssignedResourceId;
                    _logger.LogDebug(
                        "Passed assigned resource ID to entry {Index}: {AssignedId}",
                        entry.Index,
                        entry.AssignedResourceId);
                }

                // Execute through ASP.NET Core pipeline
                // This automatically routes to correct endpoint handler (FhirEndpoints)
                await _pipelineExecutor.ExecuteAsync(httpContext);

                // Extract response from HttpContext
                return await ExtractResponseAsync(httpContext, cancellationToken);
            }
            finally
            {
                // Manually dispose HttpContext if it implements IDisposable
                if (httpContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
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
                new OperationOutcomeJsonNode.IssueComponent
                {
                    Severity = severity,
                    Code = code,
                    Diagnostics = ex.Message
                }
            };
            operationOutcome.SetIssues(issueList);

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
