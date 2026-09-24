using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Application.BackgroundOperations.Jobs;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Ignixa.Serialization.Models;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// API endpoints for FHIR bulk export operations ($export).
/// Uses DurableTask framework for durable, reliable background processing.
/// </summary>
public static class ExportEndpoints
{
    /// <summary>
    /// Registers export-related endpoints with the application.
    /// </summary>
    public static void MapExportEndpoints(this WebApplication app)
    {
        // POST /$export - System-level export (single-tenant or auto-detected tenant)
        app.MapPost("/$export", StartExportSystemLevelAsync)
            .WithName("StartExportSystemLevel");

        // POST /tenant/{tenantId}/$export - Start a new export job
        app.MapPost("/tenant/{tenantId:int}/$export", StartExportAsync)
            .WithName("StartExport");

        // POST /Group/{groupId}/$export - Group-scoped export
        app.MapPost("/Group/{groupId}/$export", StartGroupExportAsync)
            .WithName("StartGroupExport");

        app.MapPost("/tenant/{tenantId:int}/Group/{groupId}/$export", StartGroupExportAsync)
            .WithName("StartGroupExportForTenant");

        // GET /tenant/{tenantId}/_export/{jobId} - Poll export job status
        app.MapGet("/tenant/{tenantId:int}/_export/{jobId}", GetExportStatusAsync)
            .WithName("GetExportStatus");

        // DELETE /tenant/{tenantId}/_export/{jobId} - Cancel export job
        app.MapDelete("/tenant/{tenantId:int}/_export/{jobId}", CancelExportAsync)
            .WithName("CancelExport");
    }

    /// <summary>
    /// Starts a new bulk export operation at the system level (auto-detects tenant).
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartExportSystemLevelAsync(
        [FromQuery(Name = "_type")] string? resourceTypes,
        [FromQuery(Name = "_since")] DateTimeOffset? since,
        [FromQuery(Name = "_typeFilter")] string? typeFilter,
        [FromQuery(Name = "_outputFormat")] string? outputFormat,
        [FromQuery(Name = "_viewDefinition")] string? viewDefinition,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        // Extract tenant ID from HttpContext (set by TenantResolutionMiddleware)
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || !(tenantIdObj is int tenantId))
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = "Unable to determine tenant from request context"
                    }
                }
            });
        }

        // Delegate to the tenant-explicit handler
        return await StartExportAsync(tenantId, resourceTypes, since, typeFilter, outputFormat, viewDefinition, mediator, httpContext);
    }

    /// <summary>
    /// Starts a Group-scoped bulk export operation.
    /// Exports only resources for patients that are members of the specified Group.
    /// </summary>
    private static async Task<IResult> StartGroupExportAsync(
        [FromRoute] string groupId,
        [FromQuery(Name = "_type")] string? resourceTypes,
        [FromQuery(Name = "_since")] DateTimeOffset? since,
        [FromQuery(Name = "_typeFilter")] string? typeFilter,
        [FromQuery(Name = "_outputFormat")] string? outputFormat,
        [FromQuery(Name = "_viewDefinition")] string? viewDefinition,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        // Extract tenant ID from HttpContext (set by TenantResolutionMiddleware)
        if (!httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) || !(tenantIdObj is int tenantId))
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = "Unable to determine tenant from request context"
                    }
                }
            });
        }

        // Validate _outputFormat (only application/fhir+ndjson and application/vnd.apache.parquet supported)
        if (!string.IsNullOrEmpty(outputFormat) &&
            outputFormat != ExportConstants.MediaTypeNdjson &&
            outputFormat != ExportConstants.MediaTypeParquet)
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "not-supported",
                        diagnostics = $"Unsupported _outputFormat: {outputFormat}. Supported formats: {ExportConstants.MediaTypeNdjson}, {ExportConstants.MediaTypeParquet}"
                    }
                }
            });
        }

        // Validate _viewDefinition parameter
        if (!string.IsNullOrEmpty(viewDefinition))
        {
            // ViewDefinition requires Parquet format
            if (outputFormat != ExportConstants.MediaTypeParquet)
            {
                return Results.BadRequest(new
                {
                    resourceType = "OperationOutcome",
                    issue = new[]
                    {
                        new
                        {
                            severity = "error",
                            code = "invalid",
                            diagnostics = $"_viewDefinition parameter requires _outputFormat={ExportConstants.MediaTypeParquet}"
                        }
                    }
                });
            }
        }

        // Validate Parquet format requires ViewDefinition (for structured schema)
        if (outputFormat == ExportConstants.MediaTypeParquet && string.IsNullOrEmpty(viewDefinition))
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = $"Parquet format (_outputFormat={ExportConstants.MediaTypeParquet}) requires _viewDefinition parameter for structured schema"
                    }
                }
            });
        }

        // Parse resource types from comma-separated query parameter
        var types = string.IsNullOrWhiteSpace(resourceTypes)
            ? Array.Empty<string>()
            : resourceTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Parse type filters from comma-separated query parameter
        var typeFilters = ParseTypeFilters(typeFilter);

        try
        {
            var command = new CreateExportJobCommand
            {
                TenantId = tenantId,
                ResourceTypes = types,
                Since = since,
                TypeFilters = typeFilters,
                OutputFormat = outputFormat ?? ExportConstants.MediaTypeNdjson,
                ViewDefinitionId = viewDefinition,
                GroupId = groupId,
                RequestUrl = httpContext.Request.GetDisplayUrl()
            };

            var result = await mediator.SendAsync(command, httpContext.RequestAborted);

            // Return 202 Accepted with Content-Location header
            var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_export/{result.JobId}";
            httpContext.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl, new { jobId = result.JobId, status = "queued" });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "not-supported",
                        diagnostics = ex.Message
                    }
                }
            });
        }
    }

    /// <summary>
    /// Starts a new bulk export operation.
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartExportAsync(
        [FromRoute] int tenantId,
        [FromQuery(Name = "_type")] string? resourceTypes,
        [FromQuery(Name = "_since")] DateTimeOffset? since,
        [FromQuery(Name = "_typeFilter")] string? typeFilter,
        [FromQuery(Name = "_outputFormat")] string? outputFormat,
        [FromQuery(Name = "_viewDefinition")] string? viewDefinition,
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
        // Validate _outputFormat (only application/fhir+ndjson and application/vnd.apache.parquet supported)
        if (!string.IsNullOrEmpty(outputFormat) &&
            outputFormat != ExportConstants.MediaTypeNdjson &&
            outputFormat != ExportConstants.MediaTypeParquet)
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "not-supported",
                        diagnostics = $"Unsupported _outputFormat: {outputFormat}. Supported formats: {ExportConstants.MediaTypeNdjson}, {ExportConstants.MediaTypeParquet}"
                    }
                }
            });
        }

        // Validate _viewDefinition parameter
        if (!string.IsNullOrEmpty(viewDefinition))
        {
            // ViewDefinition requires Parquet format
            if (outputFormat != ExportConstants.MediaTypeParquet)
            {
                return Results.BadRequest(new
                {
                    resourceType = "OperationOutcome",
                    issue = new[]
                    {
                        new
                        {
                            severity = "error",
                            code = "invalid",
                            diagnostics = $"_viewDefinition parameter requires _outputFormat={ExportConstants.MediaTypeParquet}"
                        }
                    }
                });
            }
        }

        // Validate Parquet format requires ViewDefinition (for structured schema)
        if (outputFormat == ExportConstants.MediaTypeParquet && string.IsNullOrEmpty(viewDefinition))
        {
            return Results.BadRequest(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = "invalid",
                        diagnostics = $"Parquet format (_outputFormat={ExportConstants.MediaTypeParquet}) requires _viewDefinition parameter for structured schema"
                    }
                }
            });
        }

        // Parse resource types from comma-separated query parameter
        var types = string.IsNullOrWhiteSpace(resourceTypes)
            ? Array.Empty<string>()
            : resourceTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Parse type filters from comma-separated query parameter
        var typeFilters = ParseTypeFilters(typeFilter);

        try
        {
            var command = new CreateExportJobCommand
            {
                TenantId = tenantId,
                ResourceTypes = types,
                Since = since,
                TypeFilters = typeFilters,
                OutputFormat = outputFormat ?? ExportConstants.MediaTypeNdjson,
                ViewDefinitionId = viewDefinition,
                RequestUrl = httpContext.Request.GetDisplayUrl()
            };

            var result = await mediator.SendAsync(command, httpContext.RequestAborted);

            // Return 202 Accepted with Content-Location header
            var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_export/{result.JobId}";
            httpContext.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl, new { jobId = result.JobId, status = "queued" });
        }
        catch (ArgumentException ex)
        {
            var outcome = new OperationOutcome();
            outcome.Issue.Add(new OperationOutcomeIssue
            {
                SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
                IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.NotSupported,
                Diagnostics = ex.Message
            });
            return Results.BadRequest(outcome);
        }
    }

    /// <summary>
    /// Gets the status of an export job.
    /// Returns 202 Accepted while in progress, 200 OK when complete with manifest.
    /// </summary>
    private static async Task<IResult> GetExportStatusAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] IMediator mediator,
        [FromServices] IBlobStorageClient blobStorage,
        HttpContext httpContext)
    {
        try
        {
            var query = new GetJobStatusQuery
            {
                JobId = jobId,
                JobType = "Export",
                TenantId = tenantId
            };

            var jobStatus = await mediator.SendAsync(query, httpContext.RequestAborted);
            var definition = jobStatus.Definition as ExportJobDefinition;
            var ownerTenantId = definition?.TenantId ?? tenantId;

            if (jobStatus.Status is "Queued" or "Running")
            {
                httpContext.Response.Headers.RetryAfter = "1";
                httpContext.Response.Headers["X-Progress"] = jobStatus.ProgressDescription;
            }

            // Return response based on status
            return jobStatus.Status switch
            {
                "Queued" or "Running" => Results.Accepted(
                    value: new
                    {
                        jobId = jobStatus.JobId,
                        status = jobStatus.Status,
                        progressPercentage = jobStatus.ProgressPercentage,
                        progressDescription = jobStatus.ProgressDescription
                    }),

                "Completed" => Results.Ok(new
                {
                    transactionTime = jobStatus.CreateDate,
                    request = definition?.RequestUrl
                        ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}/tenant/{ownerTenantId}/$export",
                    requiresAccessToken = false,
                    output = await BuildOutputManifestFromResultAsync(jobStatus.Result, ownerTenantId, jobStatus.JobId, blobStorage, httpContext.RequestAborted),
                    error = Array.Empty<object>(),
                }),

                "Failed" => JobFailure(jobStatus.ErrorMessage ?? "Export failed", StatusCodes.Status500InternalServerError),
                "Cancelled" => JobFailure("Export cancelled by user", StatusCodes.Status410Gone),

                _ => JobFailure($"Unexpected export job status: {jobStatus.Status}", StatusCodes.Status500InternalServerError),
            };
        }
        catch (System.Collections.Generic.KeyNotFoundException)
        {
            return Results.NotFound(new { error = "Export job not found" });
        }
        catch (JsonException)
        {
            return JobFailure("Export job has an invalid persisted result or progress record.", StatusCodes.Status500InternalServerError);
        }
        catch (FileNotFoundException)
        {
            return JobFailure("An output file for this export job is missing from blob storage.", StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Cancels an export job.
    /// </summary>
    private static async Task<IResult> CancelExportAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<ExportJobDefinition> jobRepository,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound(new { error = "Export job not found" });
        }

        var logger = loggerFactory.CreateLogger("Ignixa.Api.Endpoints.ExportEndpoints");
        if (job.Status is "Completed" or "Failed" or "Cancelled")
        {
            logger.LogInformation("Export cancellation for {JobId} is superseded by {Status}", jobId, job.Status);
            return CancellationResult(job.Status);
        }

        // Terminate the orchestration
        var instance = new OrchestrationInstance { InstanceId = jobId };
        await taskHubClient.TerminateInstanceAsync(instance, "Cancelled by user");

        job.Status = "Cancelled";
        job.EndDate = DateTimeOffset.UtcNow;
        try
        {
            await jobRepository.UpdateAsync(job, tenantId, cancellationToken);
        }
        catch (BackgroundJobUpdateConflictException)
        {
            var authoritative = await jobRepository.GetAsync(jobId, tenantId, cancellationToken);
            if (authoritative == null)
            {
                logger.LogInformation("Export job {JobId} was removed before cancellation conflict reload", jobId);
                return Results.NotFound(new { error = "Export job not found" });
            }
            logger.LogInformation(
                "Export cancellation for {JobId} lost to {Status}; preserving authoritative metadata",
                jobId, authoritative.Status);
            return CancellationResult(authoritative.Status);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Builds the FHIR Bulk Data output manifest from job result.
    /// </summary>
    private static async Task<List<object>> BuildOutputManifestFromResultAsync(
        object? result,
        int tenantId,
        string jobId,
        IBlobStorageClient blobStorage,
        CancellationToken cancellationToken)
    {
        var outputManifest = new List<object>();

        if (result is not ExportJobResult exportResult)
        {
            throw new JsonException("Completed export job has no typed result.");
        }

        foreach (var (fileKey, filePath) in exportResult.ExportedFiles)
        {
            var outputPath = filePath;
            var exists = await blobStorage.BlobExistsAsync(outputPath, cancellationToken);
            var legacyPrefix = $"tenant/{tenantId}/export/{jobId}/";
            if (!exists && exportResult.ExportedFileCounts == null &&
                filePath.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Older coordinators persisted tenant/... while their workers wrote partition/....
                outputPath = $"partition/{filePath["tenant/".Length..]}";
                exists = await blobStorage.BlobExistsAsync(outputPath, cancellationToken);
            }

            if (!exists)
            {
                throw new FileNotFoundException("Export output blob not found.", filePath);
            }
            var resourceType = fileKey.Split('-', 2)[0];
            var url = await blobStorage.GetBlobUrlAsync(outputPath, TimeSpan.FromHours(24), cancellationToken);
            if (exportResult.ExportedFileCounts is { } counts)
            {
                outputManifest.Add(new { type = resourceType, url, count = counts[fileKey] });
            }
            else
            {
                // Older persisted jobs have no per-partition counts; do not invent them.
                outputManifest.Add(new { type = resourceType, url });
            }
        }

        return outputManifest;
    }

    private static IResult CancellationResult(string status) =>
        status == "Cancelled"
            ? Results.NoContent()
            : JobFailure($"Export is already {status}; cancellation did not change its terminal outcome.",
                StatusCodes.Status409Conflict, "conflict");

    private static IResult JobFailure(string diagnostics, int statusCode, string code = "exception") =>
        Results.Json(new
        {
            resourceType = "OperationOutcome",
            issue = new[] { new { severity = "error", code, diagnostics } }
        }, contentType: "application/fhir+json", statusCode: statusCode);

    /// <summary>
    /// Parses the _typeFilter parameter into a dictionary of resource type to filter query.
    /// Format: "Observation?code=http://loinc.org|85354-9,Condition?category=encounter-diagnosis"
    /// </summary>
    private static Dictionary<string, string> ParseTypeFilters(string? typeFilter)
    {
        var filters = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(typeFilter))
        {
            return filters;
        }

        // Split by comma to get individual filters
        var filterParts = typeFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in filterParts)
        {
            // Split by '?' to separate resource type from query parameters
            var questionMarkIndex = part.IndexOf('?', StringComparison.Ordinal);
            if (questionMarkIndex > 0)
            {
                var resourceType = part.Substring(0, questionMarkIndex).Trim();
                var queryString = part.Substring(questionMarkIndex + 1).Trim();

                filters[resourceType] = queryString;
            }
        }

        return filters;
    }
}
