// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Terminology.Activities;
using Ignixa.Application.BackgroundOperations.Terminology.Models;

namespace Ignixa.Application.BackgroundOperations.Terminology.Orchestrations;

/// <summary>
/// DurableTask orchestration for FHIR terminology import from NPM packages.
/// Coordinates parallel import of CodeSystem, ValueSet, and ConceptMap resources into SQL terminology tables.
/// </summary>
public class TerminologyImportOrchestration : TaskOrchestration<TerminologyImportOrchestrationOutput, TerminologyImportOrchestrationInput>
{
    public override async Task<TerminologyImportOrchestrationOutput> RunTask(
        OrchestrationContext context,
        TerminologyImportOrchestrationInput input)
    {
        var results = new List<TerminologyImportResourceResult>();

        try
        {
            // Process terminology resources in parallel with concurrency limit
            // Limit to 5 concurrent imports to avoid overwhelming database connection pool
            // Each activity uses ITerminologyImporter which may perform bulk inserts
            const int maxConcurrent = 5;

            // Create tasks for all resources
            var allTasks = input.PackageResourceIds.Select(packageResourceId =>
            {
                var activityInput = new ImportTerminologyResourceInput(
                    TenantId: input.TenantId,
                    PackageResourceId: packageResourceId);

                return context.ScheduleTask<ImportTerminologyResourceOutput>(
                    typeof(ImportTerminologyResourceActivity),
                    activityInput);
            }).ToList();

            // Process in batches to limit concurrency
            for (int i = 0; i < allTasks.Count; i += maxConcurrent)
            {
                var batch = allTasks.Skip(i).Take(maxConcurrent).ToList();
                var batchResults = await Task.WhenAll(batch);

                results.AddRange(batchResults.Select(r => new TerminologyImportResourceResult(
                    r.PackageResourceId,
                    r.Canonical,
                    r.ResourceType,
                    r.Success,
                    r.ConceptCount,
                    r.ErrorMessage)));
            }

            // Aggregate results
            var successCount = results.Count(r => r.Success);
            var failedCount = results.Count(r => !r.Success && r.ErrorMessage != null);
            var skippedCount = results.Count(r => !r.Success && r.ErrorMessage == null);
            var totalConcepts = results.Sum(r => r.ConceptCount);

            return new TerminologyImportOrchestrationOutput(
                Success: failedCount == 0,
                TotalResourcesProcessed: results.Count,
                TotalConceptsImported: totalConcepts,
                SuccessCount: successCount,
                FailedCount: failedCount,
                SkippedCount: skippedCount,
                Results: results,
                ErrorMessage: null,
                FailurePhase: null);
        }
        catch (Exception ex)
        {
            return new TerminologyImportOrchestrationOutput(
                Success: false,
                TotalResourcesProcessed: results.Count,
                TotalConceptsImported: 0,
                SuccessCount: 0,
                FailedCount: 0,
                SkippedCount: 0,
                Results: results,
                ErrorMessage: ex.Message,
                FailurePhase: "Orchestration");
        }
    }
}
