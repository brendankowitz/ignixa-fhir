using System.Text.Json.Nodes;
using DurableTask.Core;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

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
        // POST /tenant/{tenantId}/$export - Start a new export job
        app.MapPost("/tenant/{tenantId:int}/$export", StartExportAsync)
            .WithName("StartExport")
            .WithOpenApi();

        // GET /tenant/{tenantId}/_export/{jobId} - Poll export job status
        app.MapGet("/tenant/{tenantId:int}/_export/{jobId}", GetExportStatusAsync)
            .WithName("GetExportStatus")
            .WithOpenApi();

        // DELETE /tenant/{tenantId}/_export/{jobId} - Cancel export job
        app.MapDelete("/tenant/{tenantId:int}/_export/{jobId}", CancelExportAsync)
            .WithName("CancelExport")
            .WithOpenApi();
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
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<ExportJobDefinition> jobRepository,
        HttpContext httpContext)
    {
        // Validate _outputFormat (only application/fhir+ndjson supported for now)
        if (!string.IsNullOrEmpty(outputFormat) && outputFormat != "application/fhir+ndjson")
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
                        diagnostics = $"Unsupported _outputFormat: {outputFormat}. Only 'application/fhir+ndjson' is supported."
                    }
                }
            });
        }

        // Parse resource types from comma-separated query parameter
        var types = string.IsNullOrWhiteSpace(resourceTypes)
            ? Array.Empty<string>()
            : resourceTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Parse type filters from comma-separated query parameter
        // Format: "ResourceType?param=value,AnotherType?param=value"
        var typeFilters = ParseTypeFilters(typeFilter);

        // Generate job ID
        var jobId = Guid.NewGuid().ToString();

        // Create job metadata in unified repository
        // TenantId is stored in the Definition (payload), not as a BackgroundJob property
        var job = new BackgroundJob<ExportJobDefinition>
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.Export,
            Status = "Queued",
            Definition = new ExportJobDefinition
            {
                TenantId = tenantId,
                ResourceTypes = types,
                Since = since,
                TypeFilters = typeFilters,
                OutputFormat = outputFormat ?? "application/fhir+ndjson",
                OutputPath = $"tenant/{tenantId}/export/{jobId}"
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        await jobRepository.CreateAsync(job, httpContext.RequestAborted);

        // Start the orchestration
        var orchestrationInput = new ExportOrchestrationInput(
            JobId: jobId,
            TenantId: tenantId,
            ResourceTypes: types,
            Since: since,
            TypeFilters: typeFilters);

        var instance = await taskHubClient.CreateOrchestrationInstanceAsync(
            typeof(ExportOrchestration),
            jobId, // Use jobId as instance ID for easy lookup
            orchestrationInput);

        // Update job with orchestration instance ID
        job.OrchestrationInstanceId = instance.InstanceId;
        await jobRepository.UpdateAsync(job, tenantId, httpContext.RequestAborted);

        // Return 202 Accepted with Content-Location header
        var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_export/{jobId}";

        httpContext.Response.Headers["Content-Location"] = statusUrl;

        return Results.Accepted(statusUrl, new { jobId, status = "queued" });
    }

    /// <summary>
    /// Gets the status of an export job.
    /// Returns 202 Accepted while in progress, 200 OK when complete with manifest.
    /// </summary>
    private static async Task<IResult> GetExportStatusAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<ExportJobDefinition> jobRepository,
        [FromServices] IBlobStorageClient blobStorage,
        HttpContext httpContext)
    {
        // Get job metadata (with tenant validation)
        var job = await jobRepository.GetAsync(jobId, tenantId, httpContext.RequestAborted);
        if (job == null)
        {
            return Results.NotFound(new { error = "Export job not found" });
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

        // Return response based on status
        return job.Status switch
        {
            "Queued" or "Running" => Results.Accepted(
                value: new
                {
                    jobId = job.JobId,
                    status = job.Status,
                    progress = job.Progress,
                }),

            "Completed" => Results.Ok(new
            {
                transactionTime = job.EndDate ?? job.CreateDate,
                request = $"/tenant/{tenantId}/$export",
                requiresAccessToken = false,
                output = await BuildOutputManifestAsync(job, blobStorage, httpContext.RequestAborted),
                error = Array.Empty<object>(),
            }),

            "Failed" => Results.Ok(new
            {
                transactionTime = job.EndDate ?? job.CreateDate,
                request = $"/tenant/{tenantId}/$export",
                error = new[]
                {
                    new
                    {
                        type = "OperationOutcome",
                        message = job.ErrorMessage,
                    },
                },
            }),

            _ => Results.StatusCode(500),
        };
    }

    /// <summary>
    /// Cancels an export job.
    /// </summary>
    private static async Task<IResult> CancelExportAsync(
        [FromRoute] int tenantId,
        [FromRoute] string jobId,
        [FromServices] TaskHubClient taskHubClient,
        [FromServices] IBackgroundJobRepository<ExportJobDefinition> jobRepository,
        HttpContext httpContext)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, httpContext.RequestAborted);
        if (job == null)
        {
            return Results.NotFound(new { error = "Export job not found" });
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
    /// Builds the FHIR Bulk Data output manifest.
    /// </summary>
    private static async Task<List<object>> BuildOutputManifestAsync(
        BackgroundJob<ExportJobDefinition> job,
        IBlobStorageClient blobStorage,
        CancellationToken cancellationToken)
    {
        var outputManifest = new List<object>();

        // Extract exported files from Result JSON if available
        if (job.Result != null)
        {
            var exportedFilesNode = job.Result["exportedFiles"];
            if (exportedFilesNode != null && exportedFilesNode is JsonObject filesObj)
            {
                foreach (var (resourceType, filePath) in filesObj)
                {
                    if (filePath is JsonValue pathValue && pathValue.TryGetValue(out string? path))
                    {
                        var url = await blobStorage.GetBlobUrlAsync(path, TimeSpan.FromHours(24), cancellationToken);
                        outputManifest.Add(new
                        {
                            type = resourceType,
                            url,
                        });
                    }
                }
            }
        }

        return outputManifest;
    }

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
