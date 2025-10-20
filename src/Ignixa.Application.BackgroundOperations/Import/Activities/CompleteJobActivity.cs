// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Completes import job by uploading error logs (if any) and finalizing job status.
/// </summary>
public class CompleteJobActivity : AsyncTaskActivity<CompleteJobInput, CompleteJobOutput>
{
    private readonly ILogger<CompleteJobActivity> _logger;

    public CompleteJobActivity(ILogger<CompleteJobActivity> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<CompleteJobOutput> ExecuteAsync(
        TaskContext context,
        CompleteJobInput input)
    {
        _logger.LogInformation(
            "Completing import job {JobId}: {TotalResources} resources, {TotalErrors} errors",
            input.JobId,
            input.TotalResources,
            input.TotalErrors);

        string? errorFileUrl = null;

        // Upload error logs if there are any errors
        if (input.ErrorLogEntries.Any())
        {
            errorFileUrl = await UploadErrorLogAsync(input);
        }

        _logger.LogInformation("Import job {JobId} completed", input.JobId);

        return new CompleteJobOutput
        {
            ErrorFileUrl = errorFileUrl
        };
    }

    /// <summary>
    /// Uploads error log to local file system as NDJSON.
    /// Future: Upload to blob storage using StorageDetail.
    /// </summary>
    private async Task<string> UploadErrorLogAsync(CompleteJobInput input)
    {
        try
        {
            // Convert errors to NDJSON format (one OperationOutcome per line)
            var ndjsonLines = new StringBuilder();

            foreach (var error in input.ErrorLogEntries)
            {
                var operationOutcome = new
                {
                    resourceType = "OperationOutcome",
                    issue = new[]
                    {
                        new
                        {
                            severity = "error",
                            code = error.ErrorCode,
                            diagnostics = error.ErrorMessage,
                            expression = new[] { $"{error.ResourceType}/{error.ResourceId}" }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(operationOutcome);
                ndjsonLines.AppendLine(json);
            }

            // For now: Save to local file system
            // Future: Upload to blob storage using StorageDetail parameter
            var errorDirectory = Path.Combine("import-errors");
            Directory.CreateDirectory(errorDirectory);

            var errorFileName = $"import-errors-{input.JobId}.ndjson";
            var errorFilePath = Path.Combine(errorDirectory, errorFileName);

            await File.WriteAllTextAsync(errorFilePath, ndjsonLines.ToString());

            var errorFileUrl = $"/import-errors/{errorFileName}";

            _logger.LogInformation(
                "Uploaded error log for job {JobId}: {ErrorCount} errors to {Url}",
                input.JobId,
                input.ErrorLogEntries.Count,
                errorFileUrl);

            return errorFileUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading error log for job {JobId}", input.JobId);
            throw;
        }
    }
}
