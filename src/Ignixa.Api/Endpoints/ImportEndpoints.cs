// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.BackgroundOperations.Import.Orchestrations;
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
            .WithName("StartImport")
            .WithOpenApi();

        // GET /tenant/{tenantId}/_import/{jobId} - Poll import job status
        app.MapGet("/tenant/{tenantId:int}/_import/{jobId}", GetImportStatusAsync)
            .WithName("GetImportStatus")
            .WithOpenApi();

        // DELETE /tenant/{tenantId}/_import/{jobId} - Cancel import job
        app.MapDelete("/tenant/{tenantId:int}/_import/{jobId}", CancelImportAsync)
            .WithName("CancelImport")
            .WithOpenApi();
    }

    /// <summary>
    /// Starts a new bulk import operation.
    /// Returns 202 Accepted with Content-Location header pointing to the status endpoint.
    /// </summary>
    private static async Task<IResult> StartImportAsync(
        [FromRoute] int tenantId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<ImportJobDefinition> jobRepository,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext)
    {
        // Read request body as Parameters resource
        string requestBody;
        using (var reader = new StreamReader(httpContext.Request.Body))
        {
            requestBody = await reader.ReadToEndAsync();
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

        var parameters = resource as ParametersJsonNode;
        if (parameters == null)
        {
            // Try to convert
            var json = resource.SerializeToString();
            parameters = System.Text.Json.JsonSerializer.Deserialize<ParametersJsonNode>(json);

            if (parameters == null)
            {
                return Results.BadRequest(CreateOperationOutcome(
                    "Failed to parse Parameters resource"));
            }
        }

        // Extract parameters
        var inputFormat = parameters.FindParameter("inputFormat")?.GetValueAs<string>();
        var inputSource = parameters.FindParameter("inputSource")?.GetValueAs<string>();
        var mode = parameters.FindParameter("mode")?.GetValueAs<string>() ?? "IncrementalLoad";

        // Validate inputFormat
        if (inputFormat != "application/fhir+ndjson")
        {
            return Results.BadRequest(CreateOperationOutcome(
                "Invalid inputFormat. Only 'application/fhir+ndjson' is supported."));
        }

        // Validate mode
        if (mode != "InitialLoad" && mode != "IncrementalLoad")
        {
            return Results.BadRequest(CreateOperationOutcome(
                $"Invalid mode '{mode}'. Must be 'InitialLoad' or 'IncrementalLoad'."));
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

        // Generate job ID
        var jobId = Guid.NewGuid().ToString("N");

        // Create job metadata using generic BackgroundJob<T>
        // TenantId is stored in the Definition (payload), not as a BackgroundJob property
        var job = new BackgroundJob<ImportJobDefinition>
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.Import,
            Status = "Queued",
            Definition = new ImportJobDefinition
            {
                TenantId = tenantId,
                InputFormat = inputFormat,
                InputSource = inputSource ?? "",
                Mode = mode,
                InputFiles = inputFiles
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        await jobRepository.CreateAsync(job, httpContext.RequestAborted);

        // Start the orchestration
        var storageDetailParam = parameters.FindParameter("storageDetail");
        ParametersJsonNode? storageDetail = null;
        if (storageDetailParam != null)
        {
            // Convert to ParametersJsonNode if it has nested parts
            var storageJson = System.Text.Json.JsonSerializer.Serialize(storageDetailParam);
            storageDetail = System.Text.Json.JsonSerializer.Deserialize<ParametersJsonNode>(storageJson);
        }

        // Read per-import performance tuning settings from configuration
        // Global settings (MaxConcurrentFiles, ConsumerCount) are read directly by the orchestration/activities from IConfiguration
        var batchSize = configuration.GetValue<int>("Import:BatchSize", 100);
        var channelCapacity = configuration.GetValue<int>("Import:ChannelCapacity", 1000);

        // Validate per-import configuration values
        if (batchSize < 1) batchSize = 1;
        if (channelCapacity < 1) channelCapacity = 1;

        var orchestrationInput = new ImportOrchestrationInput
        {
            JobId = jobId,
            TenantId = tenantId,
            InputFiles = inputFiles,
            Mode = mode,
            StorageDetail = storageDetail,
            BatchSize = batchSize,
            ChannelCapacity = channelCapacity
        };

        var instance = await taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(ImportOrchestration),
            jobId, // Use jobId as instance ID for easy lookup
            orchestrationInput);

        // Update job with orchestration instance ID
        job.OrchestrationInstanceId = instance.InstanceId;
        await jobRepository.UpdateAsync(job, tenantId, httpContext.RequestAborted);

        // Return 202 Accepted with Content-Location header
        var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_import/{jobId}";

        httpContext.Response.Headers["Content-Location"] = statusUrl;

        return Results.Accepted(statusUrl, new { jobId, status = "queued" });
    }

    /// <summary>
    /// Gets the status of an import job.
    /// Returns 202 Accepted while in progress, 200 OK when complete.
    /// </summary>
    private static async Task<IResult> GetImportStatusAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<ImportJobDefinition> jobRepository,
        HttpContext httpContext)
    {
        // Get job metadata (with tenant validation)
        var job = await jobRepository.GetAsync(jobId, tenantId, httpContext.RequestAborted);
        if (job == null)
        {
            return Results.NotFound(new { error = "Import job not found" });
        }

        // Get orchestration state
        var state = await taskHubClient.GetOrchestrationStateAsync(jobId);

        // Update job status based on orchestration state
        if (state != null)
        {
            switch (state.OrchestrationStatus)
            {
                case OrchestrationStatus.Running:
                case OrchestrationStatus.Pending:
                    job.Status = "Running";
                    if (job.StartDate == null)
                    {
                        job.StartDate = DateTimeOffset.UtcNow;
                    }
                    break;

                case OrchestrationStatus.Completed:
                    job.Status = "Completed";
                    job.EndDate = DateTimeOffset.UtcNow;

                    // Extract output from orchestration and store using strongly-typed POCO
                    if (state.Output != null)
                    {
                        var output = JsonSerializer.Deserialize<ImportOrchestrationOutput>(state.Output);
                        if (output != null)
                        {
                            var jobResult = new ImportJobResult
                            {
                                TotalResources = output.TotalResources,
                                TotalErrors = output.TotalErrors,
                                ErrorFileUrl = output.ErrorFileUrl
                            };
                            job.Result = JsonNode.Parse(JsonSerializer.Serialize(jobResult));
                        }
                    }
                    break;

                case OrchestrationStatus.Failed:
                    job.Status = "Failed";
                    job.EndDate = DateTimeOffset.UtcNow;
                    job.ErrorMessage = "Orchestration failed";
                    break;

                case OrchestrationStatus.Terminated:
                    job.Status = "Cancelled";
                    job.EndDate = DateTimeOffset.UtcNow;
                    break;
            }

            await jobRepository.UpdateAsync(job, tenantId, httpContext.RequestAborted);
        }

        // Deserialize progress and result from JSON to POCO classes
        var progress = job.Progress != null
            ? System.Text.Json.JsonSerializer.Deserialize<ImportJobProgress>(job.Progress.ToJsonString())
            : null;

        var result = job.Result != null
            ? System.Text.Json.JsonSerializer.Deserialize<ImportJobResult>(job.Result.ToJsonString())
            : null;

        // Return response based on status
        return job.Status switch
        {
            "Queued" or "Running" => Results.Accepted(
                value: new
                {
                    transactionTime = job.CreateDate,
                    request = $"/tenant/{tenantId}/$import",
                    requiresAccessToken = false,
                    output = Array.Empty<object>(),
                    error = Array.Empty<object>(),
                    // Phase 6: Progress tracking
                    extension = new[]
                    {
                        new
                        {
                            url = "http://hl7.org/fhir/StructureDefinition/import-progress",
                            valueString = progress != null
                                ? $"{progress.ProgressPercentage:F2}% complete ({progress.ProcessedFiles}/{job.Definition.InputFiles.Count} files, {progress.ProcessedResources} resources)"
                                : "Starting..."
                        }
                    }
                }),

            "Completed" => Results.Ok(new
            {
                transactionTime = job.CreateDate,
                request = $"/tenant/{tenantId}/$import",
                requiresAccessToken = false,
                output = new[]
                {
                    new
                    {
                        type = "OperationOutcome",
                        count = result?.TotalResources ?? 0,
                        inputUrl = job.Definition.InputSource
                    }
                },
                error = result?.ErrorFileUrl != null
                    ? new[]
                    {
                        new
                        {
                            type = "OperationOutcome",
                            url = result.ErrorFileUrl
                        }
                    }
                    : Array.Empty<object>()
            }),

            "Failed" => Results.Ok(new
            {
                transactionTime = job.CreateDate,
                request = $"/tenant/{tenantId}/$import",
                error = new[]
                {
                    new
                    {
                        type = "OperationOutcome",
                        message = job.ErrorMessage
                    }
                }
            }),

            "Cancelled" => Results.Ok(new
            {
                transactionTime = job.CreateDate,
                request = $"/tenant/{tenantId}/$import",
                error = new[]
                {
                    new
                    {
                        type = "OperationOutcome",
                        message = "Import cancelled by user"
                    }
                }
            }),

            _ => Results.StatusCode(500)
        };
    }

    /// <summary>
    /// Cancels an import job.
    /// </summary>
    private static async Task<IResult> CancelImportAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<ImportJobDefinition> jobRepository,
        HttpContext httpContext)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, httpContext.RequestAborted);
        if (job == null)
        {
            return Results.NotFound(new { error = "Import job not found" });
        }

        // Terminate the orchestration
        var instance = new OrchestrationInstance { InstanceId = jobId };
        await taskHubClient.TerminateInstanceAsync(instance, "Cancelled by user");

        job.Status = "Cancelled";
        job.EndDate = DateTimeOffset.UtcNow;
        await jobRepository.UpdateAsync(job, tenantId, httpContext.RequestAborted);

        return Results.NoContent();
    }

    /// <summary>
    /// Creates a FHIR OperationOutcome for error responses.
    /// </summary>
    private static object CreateOperationOutcome(string message)
    {
        return new
        {
            resourceType = "OperationOutcome",
            issue = new[]
            {
                new
                {
                    severity = "error",
                    code = "invalid",
                    diagnostics = message
                }
            }
        };
    }
}
