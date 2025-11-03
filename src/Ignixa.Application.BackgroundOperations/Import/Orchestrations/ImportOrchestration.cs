// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
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
        var totalResources = 0;
        var totalErrors = 0;
        var errorLogEntries = new List<ImportErrorLogEntry>();

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
                return new ImportOrchestrationOutput
                {
                    JobId = input.JobId,
                    Status = "Failed",
                    ErrorMessage = validationResult.ErrorMessage,
                    TotalResources = 0,
                    TotalErrors = 0
                };
            }

            // Step 2: Process all input files in parallel using streaming import
            // Each file gets its own Channel-based pipeline with 8 consumers
            // For >10K resources/sec: process 5+ files in parallel (5 files × 2K resources/sec = 10K+)
            var fileTasks = new List<Task<StreamingImportFileOutput>>();

            foreach (var inputFile in input.InputFiles)
            {
                var streamingInput = new StreamingImportFileInput
                {
                    JobId = input.JobId,
                    TenantId = input.TenantId,
                    FileUrl = inputFile.Url,
                    ResourceType = inputFile.Type,
                    Mode = input.Mode,
                    BatchSize = 100,        // Resources per batch
                    ConsumerCount = 8,      // Parallel consumers per file
                    ChannelCapacity = 1000  // Buffer size
                };

                // Schedule streaming import activity (runs in parallel with other files)
                var fileTask = context.ScheduleTask<StreamingImportFileOutput>(
                    typeof(StreamingImportFileActivity),
                    streamingInput);

                fileTasks.Add(fileTask);
            }

            // Wait for all files to complete
            var fileOutputs = await Task.WhenAll(fileTasks);

            // Aggregate results from all files
            var processedFiles = 0;
            foreach (var fileOutput in fileOutputs)
            {
                totalResources += fileOutput.SuccessCount;
                totalErrors += fileOutput.ErrorCount;

                if (fileOutput.Errors.Any())
                {
                    errorLogEntries.AddRange(fileOutput.Errors);
                }

                processedFiles++;

                // Update progress after each file completes
                var progressInput = new UpdateProgressInput
                {
                    JobId = input.JobId,
                    TenantId = input.TenantId,
                    ProcessedResources = totalResources,
                    ProcessedFiles = processedFiles,
                    TotalFiles = input.InputFiles.Count,
                    CurrentFile = processedFiles < input.InputFiles.Count
                        ? input.InputFiles[processedFiles].Url
                        : null
                };

                await context.ScheduleTask<bool>(
                    typeof(UpdateProgressActivity),
                    progressInput);
            }

            // Step 3: Complete job (upload error logs if any)
            var completeInput = new CompleteJobInput
            {
                JobId = input.JobId,
                TenantId = input.TenantId,
                TotalResources = totalResources,
                TotalErrors = totalErrors,
                ErrorLogEntries = errorLogEntries,
                StorageDetail = input.StorageDetail
            };

            var completeOutput = await context.ScheduleTask<CompleteJobOutput>(
                typeof(CompleteJobActivity),
                completeInput);

            return new ImportOrchestrationOutput
            {
                JobId = input.JobId,
                Status = "Completed",
                TotalResources = totalResources,
                TotalErrors = totalErrors,
                ErrorFileUrl = completeOutput.ErrorFileUrl
            };
        }
        catch (Exception ex)
        {
            // Orchestration failed - log and return error
            return new ImportOrchestrationOutput
            {
                JobId = input.JobId,
                Status = "Failed",
                ErrorMessage = ex.Message,
                TotalResources = totalResources,
                TotalErrors = totalErrors
            };
        }
    }
}
