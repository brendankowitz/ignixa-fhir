// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import;
using Ignixa.Application.BackgroundOperations.Jobs;
using Medino;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
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
        [FromServices] IMediator mediator,
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
        ParametersJsonNode? storageDetail = null;
        if (storageDetailParam != null)
        {
            var storageJson = System.Text.Json.JsonSerializer.Serialize(storageDetailParam);
            storageDetail = System.Text.Json.JsonSerializer.Deserialize<ParametersJsonNode>(storageJson);
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

            // Deserialize progress for display
            var progressText = jobStatus.ProgressDescription ?? "Starting...";
            if (jobStatus.ProgressPercentage.HasValue && jobStatus.Definition != null)
            {
                var def = jobStatus.Definition as dynamic;
                var inputFileCount = def?.inputFileCount ?? 0;
                progressText = jobStatus.ProgressDescription ?? $"{jobStatus.ProgressPercentage:F2}% complete";
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
                                valueString = progressText
                            }
                        }
                    }),

                "Completed" => Results.Ok(new
                {
                    transactionTime = jobStatus.CreateDate,
                    request = $"/tenant/{tenantId}/$import",
                    requiresAccessToken = false,
                    output = new[]
                    {
                        new
                        {
                            type = "OperationOutcome",
                            count = (jobStatus.Result as dynamic)?.totalResources ?? 0,
                            inputUrl = (jobStatus.Definition as dynamic)?.inputSource ?? ""
                        }
                    },
                    error = (jobStatus.Result as dynamic)?.errorFileUrl != null
                        ? new[]
                        {
                            new
                            {
                                type = "OperationOutcome",
                                url = (jobStatus.Result as dynamic)!.errorFileUrl
                            }
                        }
                        : Array.Empty<object>()
                }),

                "Failed" => Results.Ok(new
                {
                    transactionTime = jobStatus.CreateDate,
                    request = $"/tenant/{tenantId}/$import",
                    error = new[]
                    {
                        new
                        {
                            type = "OperationOutcome",
                            message = jobStatus.ErrorMessage
                        }
                    }
                }),

                "Cancelled" => Results.Ok(new
                {
                    transactionTime = jobStatus.CreateDate,
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
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { error = "Import job not found" });
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
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound(new { error = "Import job not found" });
        }

        // Terminate the orchestration
        var instance = new OrchestrationInstance { InstanceId = jobId };
        await taskHubClient.TerminateInstanceAsync(instance, "Cancelled by user");

        job.Status = "Cancelled";
        job.EndDate = DateTimeOffset.UtcNow;
        await jobRepository.UpdateAsync(job, tenantId, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Creates a FHIR OperationOutcome for error responses.
    /// </summary>
    private static OperationOutcomeJsonNode CreateOperationOutcome(string message)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Invalid,
            Diagnostics = message
        });
        return outcome;
    }
}
