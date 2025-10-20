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

            // Step 2: Process each input file
            var processedFiles = 0;
            foreach (var inputFile in input.InputFiles)
            {
                // Download and parse NDJSON file (streaming)
                var parseInput = new DownloadAndParseInput
                {
                    JobId = input.JobId,
                    TenantId = input.TenantId,
                    FileUrl = inputFile.Url,
                    ResourceType = inputFile.Type,
                    BatchSize = 100 // Import 100 resources at a time
                };

                var parseOutput = await context.ScheduleTask<DownloadAndParseOutput>(
                    typeof(DownloadAndParseActivity),
                    parseInput);

                // Import each batch of resources
                foreach (var batch in parseOutput.Batches)
                {
                    var batchInput = new ImportBatchInput
                    {
                        JobId = input.JobId,
                        TenantId = input.TenantId,
                        ResourceType = inputFile.Type,
                        Resources = batch.Resources,
                        Mode = input.Mode
                    };

                    var batchOutput = await context.ScheduleTask<ImportBatchOutput>(
                        typeof(ImportBatchActivity),
                        batchInput);

                    totalResources += batchOutput.SuccessCount;
                    totalErrors += batchOutput.ErrorCount;

                    if (batchOutput.Errors.Any())
                    {
                        errorLogEntries.AddRange(batchOutput.Errors);
                    }
                }

                // Phase 6: Update progress after each file
                processedFiles++;
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
