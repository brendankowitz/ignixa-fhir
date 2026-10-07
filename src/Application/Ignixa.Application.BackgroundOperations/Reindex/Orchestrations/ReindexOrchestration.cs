using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;

public sealed class ReindexOrchestration
    : TaskOrchestration<ReindexOrchestrationOutput, ReindexOrchestrationInput>
{
    public override async Task<ReindexOrchestrationOutput> RunTask(
        OrchestrationContext context,
        ReindexOrchestrationInput input)
    {
        var started = await context.ScheduleTask<StartReindexOutput>(
            typeof(StartReindexActivity),
            new StartReindexInput(input.JobId, input.TargetEventId, input.Targets));

        await context.CreateTimer(context.CurrentUtcDateTime.Add(input.BarrierDelay), true);

        var tenantTasks = input.TenantIds.Select(
            tenantId => ProcessTenantAsync(context, input, tenantId));
        var tenants = await Task.WhenAll(tenantTasks);

        var completed = await context.ScheduleTask<CompleteReindexOutput>(
            typeof(CompleteReindexActivity),
            new CompleteReindexInput(
                input.JobId,
                input.TargetEventId,
                input.Targets,
                tenants,
                started.IgnoredLifecycleEvents));

        return new ReindexOrchestrationOutput(
            completed.Success,
            tenants,
            started.IgnoredLifecycleEvents
                .Concat(completed.IgnoredLifecycleEvents)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static async Task<ReindexTenantOutput> ProcessTenantAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        int tenantId)
    {
        var failures = new List<ReindexFailedResource>();
        var failedTypes = new List<string>();
        long resourcesRead = 0;
        long resourcesReindexed = 0;
        long conflicts = 0;
        long cutoffTransactionId = -1;
        long cutoffSurrogateId = -1;

        try
        {
            var cutoff = await context.ScheduleTask<RaiseBarrierOutput>(
                typeof(RaiseBarrierActivity),
                new RaiseBarrierInput(input.JobId, tenantId, input.TargetEventId));
            cutoffTransactionId = cutoff.CutoffTransactionId;
            cutoffSurrogateId = cutoff.CutoffSurrogateId;

            var drainStarted = context.CurrentUtcDateTime;
            while (true)
            {
                var drain = await context.ScheduleTask<AwaitDrainOutput>(
                    typeof(AwaitDrainActivity),
                    new AwaitDrainInput(
                        input.JobId,
                        tenantId,
                        cutoffTransactionId,
                        drainStarted,
                        input.DrainWarningAfter ?? TimeSpan.FromMinutes(5)));
                if (drain.IsDrained)
                {
                    break;
                }

                await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(1), true);
            }

            foreach (var resourceType in input.ResourceTypes)
            {
                try
                {
                    long cursor = -1;
                    do
                    {
                        var plan = await context.ScheduleTask<PlanReindexOutput>(
                            typeof(PlanReindexActivity),
                            new PlanReindexInput(
                                input.JobId,
                                tenantId,
                                resourceType,
                                cursor,
                                cutoffSurrogateId,
                                input.Parameters.MaximumNumberOfResourcesPerQuery,
                                Math.Max(1, input.ContinueAsNewThreshold)));
                        foreach (var wave in plan.Ranges.Chunk(input.Parameters.MaximumConcurrency))
                        {
                            var retry = new RetryOptions(TimeSpan.FromSeconds(1), 5)
                            {
                                BackoffCoefficient = 2,
                                MaxRetryInterval = TimeSpan.FromSeconds(30)
                            };
                            var tasks = wave.Select(range =>
                                context.ScheduleWithRetry<ReindexRangeOutput>(
                                    typeof(ReindexRangeActivity),
                                    retry,
                                    new ReindexRangeInput(
                                        input.JobId,
                                        tenantId,
                                        resourceType,
                                        range.Start,
                                        range.End,
                                        input.TargetEventId,
                                        input.Parameters.MaximumNumberOfResourcesPerWrite,
                                        input.Parameters.QueryDelayIntervalInMilliseconds)));
                            var outputs = await Task.WhenAll(tasks);
                            foreach (var output in outputs)
                            {
                                resourcesRead += output.ResourcesRead;
                                resourcesReindexed += output.ResourcesReindexed;
                                conflicts += output.Conflicts;
                                foreach (var failure in output.FailedResources.Take(100 - failures.Count))
                                {
                                    failures.Add(failure);
                                }
                            }
                        }

                        cursor = plan.NextStartAfter ?? -1;
                    }
                    while (cursor >= 0);
                }
                catch (Exception ex)
                {
                    failedTypes.Add(resourceType);
                    if (failures.Count < 100)
                    {
                        failures.Add(new ReindexFailedResource(resourceType, string.Empty, ex.Message));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return new ReindexTenantOutput(
                tenantId,
                false,
                cutoffTransactionId,
                cutoffSurrogateId,
                resourcesRead,
                resourcesReindexed,
                conflicts,
                failures,
                ex.Message)
            {
                FailedResourceTypes = input.ResourceTypes
            };
        }

        return new ReindexTenantOutput(
            tenantId,
            failedTypes.Count == 0 && failures.Count == 0,
            cutoffTransactionId,
            cutoffSurrogateId,
            resourcesRead,
            resourcesReindexed,
            conflicts,
            failures,
            failedTypes.Count == 0 ? null : "One or more resource types failed.")
        {
            FailedResourceTypes = failedTypes
        };
    }
}
