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
            const int maxConcurrent = 5;

            if (input.DependencyPlan is not null)
            {
                await RunDependencyPlanAsync(context, input, results, maxConcurrent);
            }
            else
            {
                // Persisted legacy jobs committed all schedules eagerly. Preserve their activity order;
                // only newly created jobs carry a dependency plan and use bounded scheduling.
                var allTasks = input.PackageResourceIds.Select(packageResourceId =>
                {
                    var activityInput = new ImportTerminologyResourceInput(
                        TenantId: input.TenantId,
                        PackageResourceId: packageResourceId);

                    return context.ScheduleTask<ImportTerminologyResourceOutput>(
                        typeof(ImportTerminologyResourceActivity),
                        activityInput);
                }).ToList();

                for (int i = 0; i < allTasks.Count; i += maxConcurrent)
                {
                    var batch = allTasks.Skip(i).Take(maxConcurrent).ToList();
                    var batchResults = await Task.WhenAll(batch);

                    results.AddRange(batchResults.Select(ToResult));
                }
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

    private static async Task RunDependencyPlanAsync(
        OrchestrationContext context,
        TerminologyImportOrchestrationInput input,
        List<TerminologyImportResourceResult> results,
        int maxConcurrent)
    {
        var plan = input.DependencyPlan!;
        var byId = plan.ToDictionary(resource => resource.PackageResourceId);
        if (byId.Count != input.PackageResourceIds.Count || !byId.Keys.ToHashSet().SetEquals(input.PackageResourceIds))
        {
            throw new InvalidOperationException("Terminology dependency plan does not match the requested resource IDs.");
        }
        if (plan.Any(resource => resource.DependsOn.Any(id => !byId.ContainsKey(id))))
        {
            throw new InvalidOperationException("Terminology dependency plan references a resource outside this job.");
        }

        var remainingDependencies = plan.ToDictionary(resource => resource.PackageResourceId, resource => resource.DependsOn.Count);
        var dependents = plan.SelectMany(resource => resource.DependsOn.Select(id => (Dependency: id, Resource: resource)))
            .ToLookup(edge => edge.Dependency, edge => edge.Resource);
        var ready = new Queue<TerminologyImportDependency>(plan.Where(resource => remainingDependencies[resource.PackageResourceId] == 0));
        var completed = new Dictionary<long, ImportTerminologyResourceOutput>();
        while (completed.Count < plan.Count)
        {
            if (ready.Count == 0)
            {
                foreach (var blocked in plan.Where(resource => !completed.ContainsKey(resource.PackageResourceId)).Chunk(maxConcurrent))
                {
                    var failures = await Task.WhenAll(blocked.Select(resource =>
                        context.ScheduleTask<ImportTerminologyResourceOutput>(typeof(ImportTerminologyResourceActivity),
                            new ImportTerminologyResourceInput(input.TenantId, resource.PackageResourceId,
                                "Unresolvable in-package terminology dependency cycle."))));
                    results.AddRange(failures.Select(ToResult));
                }
                return;
            }

            var batch = new List<TerminologyImportDependency>(maxConcurrent);
            while (batch.Count < maxConcurrent && ready.TryDequeue(out var resource))
            {
                batch.Add(resource);
            }
            var tasks = batch.Select(resource =>
            {
                var failedDependencies = resource.DependsOn.Where(id => !completed[id].Success).ToArray();
                var failure = failedDependencies.Length == 0
                    ? null
                    : $"In-package terminology dependencies failed: {string.Join(", ", failedDependencies)}.";
                return context.ScheduleTask<ImportTerminologyResourceOutput>(typeof(ImportTerminologyResourceActivity),
                    new ImportTerminologyResourceInput(input.TenantId, resource.PackageResourceId, failure));
            }).ToArray();
            var outputs = await Task.WhenAll(tasks);
            for (var index = 0; index < batch.Count; index++)
            {
                var id = batch[index].PackageResourceId;
                var output = outputs[index];
                if (output.PackageResourceId != id)
                {
                    throw new InvalidOperationException("Terminology activity returned a different package resource ID.");
                }
                completed.Add(id, output);
                results.Add(ToResult(output));
                foreach (var dependent in dependents[id])
                {
                    if (--remainingDependencies[dependent.PackageResourceId] == 0)
                    {
                        ready.Enqueue(dependent);
                    }
                }
            }
        }
    }

    private static TerminologyImportResourceResult ToResult(ImportTerminologyResourceOutput result)
        => new(result.PackageResourceId, result.Canonical, result.ResourceType, result.Success, result.ConceptCount, result.ErrorMessage);
}
