// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.Common;
using DurableTask.Core;
using DurableTask.Core.Exceptions;
using Ignixa.Application.BackgroundOperations.Import.Activities;
using Ignixa.Domain.Models;
using Ignixa.Application.BackgroundOperations.Import.Models;

namespace Ignixa.Application.BackgroundOperations.Import.Orchestrations;

/// <summary>
/// DurableTask orchestration for FHIR bulk data import.
/// Coordinates file download, parsing, and batch import of resources.
/// </summary>
public class ImportOrchestration : TaskOrchestration<ImportOrchestrationOutput, ImportOrchestrationInput>
{
    public override async Task<ImportOrchestrationOutput> RunTask(
        OrchestrationContext context,
        ImportOrchestrationInput input)
    {
        var startDate = context.CurrentUtcDateTime;
        var totalResources = 0;
        var totalErrors = 0;
        var errorLogEntries = new List<ImportErrorLogEntry>();
        var processedFiles = 0;
        var filesScheduled = false;
        var countsAreComplete = true;
        int? resourcesWithUnknownOutcome = 0;
        string? failureMessage = null;

        Task<StreamingImportFileOutput>[] ScheduleFiles(IEnumerable<InputFileInfo> files) =>
            files.Select(inputFile => context.ScheduleTask<StreamingImportFileOutput>(
                typeof(StreamingImportFileActivity),
                new StreamingImportFileInput
                {
                    JobId = input.JobId,
                    TenantId = input.TenantId,
                    FileUrl = inputFile.Url,
                    ResourceType = inputFile.Type,
                    Mode = input.Mode,
                    BatchSize = input.BatchSize,
                    ChannelCapacity = input.ChannelCapacity
                })).ToArray();

        async Task<bool> RecordOutputsAsync(IReadOnlyList<StreamingImportFileOutput> outputs)
        {
            var checkpointResources = totalResources;
            // All these file activities have completed; retain their counts even if checkpointing fails.
            foreach (var output in outputs)
            {
                totalResources += output.SuccessCount;
                totalErrors += output.ErrorCount;
                errorLogEntries.AddRange(output.Errors);
            }

            var checkpointed = true;
            foreach (var output in outputs)
            {
                processedFiles++;
                checkpointResources += output.SuccessCount;
                var accepted = await context.ScheduleTask<bool>(
                    typeof(UpdateProgressActivity),
                    new UpdateProgressInput
                    {
                        JobId = input.JobId,
                        TenantId = input.TenantId,
                        ProcessedResources = checkpointResources,
                        ProcessedFiles = processedFiles,
                        TotalFiles = input.InputFiles.Count,
                        CurrentFile = processedFiles < input.InputFiles.Count ? input.InputFiles[processedFiles].Url : null
                    });
                if (!accepted)
                {
                    checkpointed = false;
                    if (input.BoundedFileScheduling)
                    {
                        break;
                    }
                }
            }
            return checkpointed;
        }

        try
        {
            // Step 1: Validate files (ETag checks, existence)
            var validateInput = new ValidateFileInput
            {
                JobId = input.JobId,
                TenantId = input.TenantId,
                InputFiles = input.InputFiles
            };

            var validationResult = await context.ScheduleTask<ValidateFileOutput>(
                typeof(ValidateFileActivity),
                validateInput);

            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException(validationResult.ErrorMessage ?? "Import file validation failed.");
            }

            if (input.BoundedFileScheduling)
            {
                if (input.MaxConcurrentFiles < 1)
                {
                    throw new InvalidOperationException("Import MaxConcurrentFiles must be positive.");
                }

                foreach (var inputBatch in input.InputFiles.Chunk(input.MaxConcurrentFiles))
                {
                    filesScheduled = true;
                    var outputs = await Task.WhenAll(ScheduleFiles(inputBatch));
                    if (!await RecordOutputsAsync(outputs))
                    {
                        failureMessage = "Import progress checkpoint was not accepted; further file scheduling stopped.";
                        countsAreComplete = false;
                        break;
                    }
                }
            }
            else
            {
                // Preserve old history: schedule all files first, await pairs, then checkpoint in input order.
                filesScheduled = true;
                var tasks = ScheduleFiles(input.InputFiles);
                var outputs = new List<StreamingImportFileOutput>();
                foreach (var taskBatch in tasks.Chunk(2))
                {
                    outputs.AddRange(await Task.WhenAll(taskBatch));
                }

                if (!await RecordOutputsAsync(outputs))
                {
                    failureMessage = "Import progress checkpoint was not accepted.";
                }
            }
        }
        catch (Exception ex) when (ex is TaskFailedException or TaskFailureException or IOException
            or DbException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            // This is the workflow's fatal-error boundary, not a record-rejection fallback.
            countsAreComplete = false;
            resourcesWithUnknownOutcome = filesScheduled ? null : 0;
            failureMessage = $"{ex.Message} Import stopped. Counts are confirmed lower bounds from completed files; " +
                (filesScheduled ? "other write/commit outcomes are unknown." : "no file writes were scheduled.");
        }

        // Finalization is outside the processing catch: a storage failure here must not re-enter it.
        var completeOutput = await context.ScheduleTask<CompleteJobOutput>(
            typeof(CompleteJobActivity),
            new CompleteJobInput
            {
                JobId = input.JobId,
                TenantId = input.TenantId,
                TotalResources = totalResources,
                TotalErrors = totalErrors,
                ErrorLogEntries = errorLogEntries,
                StorageDetail = input.StorageDetail,
                StartDate = startDate,
                ErrorMessage = failureMessage,
                CountsAreComplete = countsAreComplete,
                ResourcesWithUnknownOutcome = resourcesWithUnknownOutcome
            });
        return new ImportOrchestrationOutput
        {
            JobId = input.JobId,
            Status = completeOutput.Status ?? (failureMessage == null ? "Completed" : "Failed"),
            ErrorMessage = completeOutput.Status == null ? failureMessage : completeOutput.ErrorMessage,
            TotalResources = completeOutput.Result?.TotalResources ?? totalResources,
            TotalErrors = completeOutput.Result?.TotalErrors ?? totalErrors,
            CountsAreComplete = completeOutput.Result?.CountsAreComplete
                ?? (completeOutput.Status == null && countsAreComplete),
            ResourcesWithUnknownOutcome = completeOutput.Result != null
                ? completeOutput.Result.ResourcesWithUnknownOutcome
                : completeOutput.Status == null ? resourcesWithUnknownOutcome : null,
            ErrorFileUrl = completeOutput.ErrorFileUrl
        };
    }
}
