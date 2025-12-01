using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Application.BackgroundOperations.Jobs;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
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
        [FromServices] IMediator mediator,
        HttpContext httpContext)
    {
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
                OutputFormat = outputFormat ?? "application/fhir+ndjson"
            };

            var result = await mediator.SendAsync(command, httpContext.RequestAborted);

            // Return 202 Accepted with Content-Location header
            var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/tenant/{tenantId}/_export/{result.JobId}";
            httpContext.Response.Headers["Content-Location"] = statusUrl;

            return Results.Accepted(statusUrl, new { jobId = result.JobId, status = "queued" });
        }
        catch (ArgumentException ex)
        {
            var outcome = new OperationOutcomeJsonNode();
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
                Code = OperationOutcomeJsonNode.IssueType.NotSupported,
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
                    transactionTime = jobStatus.EndDate ?? jobStatus.CreateDate,
                    request = $"/tenant/{tenantId}/$export",
                    requiresAccessToken = false,
                    output = await BuildOutputManifestFromResultAsync(jobStatus.Result, blobStorage, httpContext.RequestAborted),
                    error = Array.Empty<object>(),
                }),

                "Failed" => Results.Ok(new
                {
                    transactionTime = jobStatus.EndDate ?? jobStatus.CreateDate,
                    request = $"/tenant/{tenantId}/$export",
                    error = new[]
                    {
                        new
                        {
                            type = "OperationOutcome",
                            message = jobStatus.ErrorMessage,
                        },
                    },
                }),

                _ => Results.StatusCode(500),
            };
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { error = "Export job not found" });
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
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetAsync(jobId, tenantId, cancellationToken);
        if (job == null)
        {
            return Results.NotFound(new { error = "Export job not found" });
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
    /// Builds the FHIR Bulk Data output manifest from job result.
    /// </summary>
    private static async Task<List<object>> BuildOutputManifestFromResultAsync(
        object? result,
        IBlobStorageClient blobStorage,
        CancellationToken cancellationToken)
    {
        var outputManifest = new List<object>();

        if (result != null)
        {
            var resultDynamic = result as dynamic;
            var outputFiles = resultDynamic?.outputFiles as Dictionary<string, string>;

            if (outputFiles != null)
            {
                foreach (var (resourceType, filePath) in outputFiles)
                {
                    var url = await blobStorage.GetBlobUrlAsync(filePath, TimeSpan.FromHours(24), cancellationToken);
                    outputManifest.Add(new
                    {
                        type = resourceType,
                        url,
                    });
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
