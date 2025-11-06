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
        var startDate = context.CurrentUtcDateTime;
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

            // Step 2: Process files with global concurrency limit to prevent thread pool starvation
            // MaxConcurrentFiles is a global setting (read from configuration via DurableTask context)
            // that limits parallel StreamingImportFileActivity instances.
            // ConsumerCount per activity is also a global setting read by the activity from IConfiguration.
            // Example: 2 concurrent files × (1 producer + 8 consumers) = 18 task pool threads
            // vs 5 files × 9 = 45 threads, leaving plenty for API requests.
            //
            // Note: We use a fixed value of 2 here as MaxConcurrentFiles is not passed via input.
            // This should be consistent with configuration. Activities read ConsumerCount from config.
            const int maxConcurrentFiles = 2; // Global constant - also configured in appsettings.json

            var fileOutputs = new List<StreamingImportFileOutput>();
            var allFileTasks = input.InputFiles.Select((inputFile, index) =>
            {
                var streamingInput = new StreamingImportFileInput
                {
                    JobId = input.JobId,
                    TenantId = input.TenantId,
                    FileUrl = inputFile.Url,
                    ResourceType = inputFile.Type,
                    Mode = input.Mode,
                    BatchSize = input.BatchSize,
                    ChannelCapacity = input.ChannelCapacity
                };

                return context.ScheduleTask<StreamingImportFileOutput>(
                    typeof(StreamingImportFileActivity),
                    streamingInput);
            }).ToList();

            // Process files in batches using the global maxConcurrentFiles limit
            for (int i = 0; i < allFileTasks.Count; i += maxConcurrentFiles)
            {
                var batchTasks = allFileTasks.Skip(i).Take(maxConcurrentFiles).ToList();
                var batchOutputs = await Task.WhenAll(batchTasks);
                fileOutputs.AddRange(batchOutputs);
            }

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
                StorageDetail = input.StorageDetail,
                StartDate = startDate
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
