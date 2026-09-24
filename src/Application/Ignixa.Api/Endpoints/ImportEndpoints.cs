// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import;
using Ignixa.Application.BackgroundOperations.Jobs;
using Medino;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// API endpoints for FHIR bulk import operations ($import).
/// Uses DurableTask framework for durable, reliable background processing.
/// </summary>
public static class ImportEndpoints
{
    /// <summary>
    /// Registers import-related endpoints with the application.
    /// </summary>
    public static void MapImportEndpoints(this WebApplication app)
    {
        // POST /tenant/{tenantId}/$import - Start a new import job
        app.MapPost("/tenant/{tenantId:int}/$import", StartImportAsync)
            .WithName("StartImport");

        // GET /tenant/{tenantId}/_import/{jobId} - Poll import job status
        app.MapGet("/tenant/{tenantId:int}/_import/{jobId}", GetImportStatusAsync)
            .WithName("GetImportStatus");

        // DELETE /tenant/{tenantId}/_import/{jobId} - Cancel import job
        app.MapDelete("/tenant/{tenantId:int}/_import/{jobId}", CancelImportAsync)
            .WithName("CancelImport");
    }

    /// <summary>
    /// Starts a new bulk import operation.
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartImportAsync(
        [FromRoute] int tenantId,
        [FromServices] IMediator mediator,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext)
    {
        // Read request body as Parameters resource
        string requestBody;
        using (var reader = new StreamReader(httpContext.Request.Body))
        {
            requestBody = await reader.ReadToEndAsync(httpContext.RequestAborted);
        }

        // Parse as ResourceJsonNode first
        ResourceJsonNode resource;
        try
        {
            resource = JsonSourceNodeFactory.Parse(requestBody);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Invalid request body. Expected FHIR Parameters resource: " + ex.Message));
        }

        // Verify it's a Parameters resource
        if (resource.ResourceType != "Parameters")
        {
            return Results.BadRequest(CreateOperationOutcome(
                $"Expected Parameters resource, got {resource.ResourceType}"));
        }

        var parameters = resource as Parameters;
        if (parameters == null)
        {
            // Try to convert
            var json = resource.SerializeToString();
            parameters = System.Text.Json.JsonSerializer.Deserialize<Parameters>(json);

            if (parameters == null)
            {
                return Results.BadRequest(CreateOperationOutcome(
                    "Failed to parse Parameters resource"));
            }
        }

        // Extract parameters
        var inputFormat = parameters.FindParameter("inputFormat")?.GetValueAs<string>();
        var mode = parameters.FindParameter("mode")?.GetValueAs<string>() ?? "IncrementalLoad";

        // Validate inputFormat
        if (inputFormat != "application/fhir+ndjson")
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Invalid inputFormat. Only 'application/fhir+ndjson' is supported."));
        }

        // Extract input files
        var inputFiles = new List<InputFileInfo>();
        var inputParameters = parameters.Parameter.Where(p => p.Name == "input");
        foreach (var inputParam in inputParameters)
        {
            var type = inputParam.FindPart("type")?.GetValueAs<string>();
            var url = inputParam.FindPart("url")?.GetValueAs<string>();
            var etag = inputParam.FindPart("etag")?.GetValueAs<string>();

            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(url))
            {
                return Results.BadRequest(CreateOperationOutcome(
                    "Each input parameter must have 'type' and 'url' parts."));
            }

            inputFiles.Add(new InputFileInfo
            {
                Type = type,
                Url = url,
                ETag = etag
            });
        }

        if (!inputFiles.Any())
        {
            return Results.BadRequest(CreateOperationOutcome(
                "At least one input file must be specified."));
        }

        // Extract storage detail if present
        var storageDetailParam = parameters.FindParameter("storageDetail");
        Parameters? storageDetail = null;
        if (storageDetailParam != null)
        {
            var storageJson = System.Text.Json.JsonSerializer.Serialize(storageDetailParam);
            storageDetail = System.Text.Json.JsonSerializer.Deserialize<Parameters>(storageJson);
        }

        // Read per-import performance tuning settings from configuration
        var batchSize = configuration.GetValue<int>("Import:BatchSize", 100);
        var channelCapacity = configuration.GetValue<int>("Import:ChannelCapacity", 1000);

        // Create import job via handler
        try
        {
            var command = new CreateImportJobCommand
            {
                TenantId = tenantId,
                InputFiles = inputFiles,
                Mode = mode,
                BatchSize = batchSize,
                ChannelCapacity = channelCapacity,
                StorageDetail = storageDetail
            };

            var result = await mediator.SendAsync(command, httpContext.RequestAborted);

            // Return 202 Accepted with Content-Location header
            var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_import/{result.JobId}";
            httpContext.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl, new { jobId = result.JobId, status = "queued" });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(CreateOperationOutcome(ex.Message));
        }
    }

    /// <summary>
    /// Gets the status of an import job.
    /// Returns 202 Accepted while in progress, 200 OK when complete.
    /// </summary>
    private static async Task<IResult> GetImportStatusAsync(
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
                JobType = "Import",
                TenantId = tenantId
            };

            var jobStatus = await mediator.SendAsync(query, httpContext.RequestAborted);

            if (jobStatus.Status == "Completed" && jobStatus.Result is ImportJobResult { ErrorFileUrl: not null } completed &&
                !await blobStorage.BlobExistsAsync(completed.ErrorFileUrl, httpContext.RequestAborted))
            {
                return JobFailure("The import error artifact is missing from blob storage.", StatusCodes.Status500InternalServerError);
            }

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
                        transactionTime = jobStatus.CreateDate,
                        request = $"/tenant/{tenantId}/$import",
                        requiresAccessToken = false,
                        output = Array.Empty<object>(),
                        error = Array.Empty<object>(),
                        extension = new[]
                        {
                            new
                            {
                                url = "http://hl7.org/fhir/StructureDefinition/import-progress",
                                valueString = jobStatus.ProgressDescription ?? "Starting..."
                            }
                        }
                    }),

                "Completed" when jobStatus.Result is ImportJobResult result => Results.Ok(new
                {
                    transactionTime = jobStatus.CreateDate,
                    request = $"/tenant/{tenantId}/$import",
                    requiresAccessToken = false,
                    output = new[]
                    {
                        new
                        {
                            type = "OperationOutcome",
                            count = result.TotalResources
                        }
                    },
                    error = result.ErrorFileUrl != null
                        ? new[]
                        {
                            new
                            {
                                type = "OperationOutcome",
                                url = await blobStorage.GetBlobUrlAsync(result.ErrorFileUrl, TimeSpan.FromHours(24), httpContext.RequestAborted),
                                count = result.TotalErrors
                            }
                        }
                        : Array.Empty<object>()
                }),

                "Failed" => JobFailure(jobStatus.ErrorMessage ?? "Import failed", StatusCodes.Status500InternalServerError),
                "Cancelled" => JobFailure("Import cancelled by user", StatusCodes.Status410Gone),
                _ => JobFailure($"Unexpected import job status or result: {jobStatus.Status}", StatusCodes.Status500InternalServerError)
            };
        }
        catch (System.Collections.Generic.KeyNotFoundException)
        {
            return Results.NotFound(new { error = "Import job not found" });
        }
        catch (JsonException)
        {
            return JobFailure("Import job has an invalid persisted result or progress record.", StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Cancels an import job.
    /// </summary>
    private static async Task<IResult> CancelImportAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<ImportJobDefinition> jobRepository,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound(new { error = "Import job not found" });
        }

        var logger = loggerFactory.CreateLogger("Ignixa.Api.Endpoints.ImportEndpoints");
        if (job.Status is "Completed" or "Failed" or "Cancelled")
        {
            logger.LogInformation("Import cancellation for {JobId} is superseded by {Status}", jobId, job.Status);
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
                logger.LogInformation("Import job {JobId} was removed before cancellation conflict reload", jobId);
                return Results.NotFound(new { error = "Import job not found" });
            }
            logger.LogInformation(
                "Import cancellation for {JobId} lost to {Status}; preserving authoritative metadata",
                jobId, authoritative.Status);
            return CancellationResult(authoritative.Status);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Creates a FHIR OperationOutcome for error responses.
    /// </summary>
    private static OperationOutcome CreateOperationOutcome(string message)
    {
        var outcome = new OperationOutcome();
        outcome.Issue.Add(new OperationOutcomeIssue
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Invalid,
            Diagnostics = message
        });
        return outcome;
    }

    private static IResult CancellationResult(string status) =>
        status == "Cancelled"
            ? Results.NoContent()
            : JobFailure($"Import is already {status}; cancellation did not change its terminal outcome.",
                StatusCodes.Status409Conflict, "conflict");

    private static IResult JobFailure(string diagnostics, int statusCode, string code = "exception") =>
        Results.Json(new
        {
            resourceType = "OperationOutcome",
            issue = new[] { new { severity = "error", code, diagnostics } }
        }, contentType: "application/fhir+json", statusCode: statusCode);
}
