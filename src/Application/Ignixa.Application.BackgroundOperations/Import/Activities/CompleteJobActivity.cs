// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Completes import job by uploading error logs (if any) and finalizing job status.
/// </summary>
public class CompleteJobActivity : AsyncTaskActivity<CompleteJobInput, CompleteJobOutput>
{
    private readonly ILogger<CompleteJobActivity> _logger;
    private readonly IBackgroundJobRepository<ImportJobDefinition> _jobRepository;
    private readonly IBlobStorageClient _blobStorage;

    public CompleteJobActivity(
        IBackgroundJobRepository<ImportJobDefinition> jobRepository,
        IBlobStorageClient blobStorage,
        ILogger<CompleteJobActivity> logger)
    {
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<CompleteJobOutput> ExecuteAsync(
        TaskContext context,
        CompleteJobInput input)
    {
        var job = await _jobRepository.GetAsync(input.JobId, input.TenantId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Import job {input.JobId} not found for tenant {input.TenantId}.");
        if (job.Status is "Completed" or "Failed" or "Cancelled")
        {
            _logger.LogInformation("Import completion for {JobId} is superseded by {Status}", input.JobId, job.Status);
            return FromAuthoritativeJob(job);
        }

        _logger.LogInformation(
            "Completing import job {JobId}: {TotalResources} resources, {TotalErrors} errors",
            input.JobId,
            input.TotalResources,
            input.TotalErrors);

        string? errorFileUrl = null;
        var errorMessage = input.ErrorMessage;
        if (!input.CountsAreComplete || input.ResourcesWithUnknownOutcome != 0)
        {
            errorMessage ??= "Import results are incomplete; storage outcomes remain unknown.";
        }

        // Upload error logs if there are any errors
        if (input.ErrorLogEntries.Any())
        {
            try
            {
                errorFileUrl = await UploadErrorLogAsync(input);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException
                or Azure.RequestFailedException or TimeoutException or OperationCanceledException)
            {
                _logger.LogError(ex, "Import error artifact upload failed for {JobId}; persisting Failed metadata without an artifact", input.JobId);
                var uploadFailure = $"Error artifact upload failed: {ex.Message}";
                errorMessage = errorMessage == null ? uploadFailure : $"{errorMessage} {uploadFailure}";
            }
        }

        // Create result using strongly-typed POCO
        var result = new ImportJobResult
        {
            TotalResources = input.TotalResources,
            TotalErrors = input.TotalErrors,
            ErrorFileUrl = errorFileUrl,
            CountsAreComplete = input.CountsAreComplete,
            ResourcesWithUnknownOutcome = input.ResourcesWithUnknownOutcome
        };

        job.Status = errorMessage == null ? "Completed" : "Failed";
        job.ErrorMessage = errorMessage;
        job.Result = JsonSerializer.SerializeToNode(result);
        job.EndDate ??= DateTimeOffset.UtcNow;
        try
        {
            await _jobRepository.UpdateAsync(job, input.TenantId, CancellationToken.None);
        }
        catch (BackgroundJobUpdateConflictException)
        {
            var authoritative = await _jobRepository.GetAsync(input.JobId, input.TenantId, CancellationToken.None)
                ?? throw new InvalidOperationException($"Import job {input.JobId} disappeared after a terminal update conflict.");
            var output = FromAuthoritativeJob(authoritative);
            if (errorFileUrl != null && errorFileUrl != output.ErrorFileUrl)
            {
                // Only the explicit conflict proves this attempt did not publish its artifact.
                await _blobStorage.DeleteBlobAsync(errorFileUrl, CancellationToken.None);
            }
            _logger.LogInformation(
                "Import completion for {JobId} was superseded; preserving {Status}",
                input.JobId, authoritative.Status);
            return output;
        }

        _logger.LogInformation(
            "Import job {JobId} finalized as {Status}: {TotalResources} resources, {TotalErrors} errors",
            input.JobId, job.Status, input.TotalResources, input.TotalErrors);
        return FromAuthoritativeJob(job);
    }

    private static CompleteJobOutput FromAuthoritativeJob(BackgroundJob<ImportJobDefinition> job)
    {
        var result = job.Result?.Deserialize<ImportJobResult>(JsonSerializerOptions.Web);
        if (job.Status == "Completed" && result == null)
        {
            throw new JsonException("Completed import job has no persisted result.");
        }
        return new CompleteJobOutput
        {
            Status = job.Status,
            ErrorMessage = job.ErrorMessage,
            ErrorFileUrl = result?.ErrorFileUrl,
            Result = result
        };
    }

    /// <summary>
    /// Stores NDJSON OperationOutcomes with the same blob provider as import/export data.
    /// </summary>
    private async Task<string> UploadErrorLogAsync(CompleteJobInput input)
    {
        var ndjsonLines = new StringBuilder();
        foreach (var error in input.ErrorLogEntries)
        {
            ndjsonLines.AppendLine(JsonSerializer.Serialize(new
            {
                resourceType = "OperationOutcome",
                issue = new[]
                {
                    new
                    {
                        severity = "error",
                        code = error.ErrorCode == "BatchWriteError" ? "exception" : "invalid",
                        diagnostics = $"{error.ErrorCode}: {error.ErrorMessage}",
                        expression = new[] { $"{error.ResourceType}/{error.ResourceId}" }
                    }
                }
            }));
        }

        var path = $"partition/{input.TenantId}/import/{input.JobId}/errors-{Guid.NewGuid():N}.ndjson";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(ndjsonLines.ToString()));
        await _blobStorage.WriteBlobAsync(path, content, CancellationToken.None);
        return path;
    }
}
