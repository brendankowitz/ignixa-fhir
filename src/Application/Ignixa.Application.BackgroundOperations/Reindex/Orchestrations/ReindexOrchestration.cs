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
        var state = input.State ?? ReindexOrchestrationState.Create(input.TenantIds);
        var scheduledActivities = 0;

        if (!state.Started)
        {
            var started = await context.ScheduleTask<StartReindexOutput>(
                typeof(StartReindexActivity),
                new StartReindexInput(input.JobId, input.TargetEventId, input.Targets, input.TenantIds));
            scheduledActivities++;
            state = state with
            {
                Started = true,
                IgnoredLifecycleEvents = started.IgnoredLifecycleEvents
            };
            ContinueIfNeeded(context, input, state, scheduledActivities);
        }

        if (!state.BarrierDelayCompleted)
        {
            await context.CreateTimer(context.CurrentUtcDateTime.Add(input.BarrierDelay), true);
            state = state with { BarrierDelayCompleted = true };
        }

        while (state.Tenants.Any(tenant => !tenant.IsCompleted))
        {
            var tenantTasks = state.Tenants.Select(
                tenant => AdvanceTenantAsync(context, input, tenant));
            var advances = await Task.WhenAll(tenantTasks);
            scheduledActivities += advances.Sum(advance => advance.ScheduledActivities);
            state = state with
            {
                Tenants = advances.Select(advance => advance.State).ToArray()
            };
            ContinueIfNeeded(context, input, state, scheduledActivities);
        }

        var tenants = state.Tenants.Select(tenant => tenant.ToOutput()).ToArray();

        var completed = await context.ScheduleTask<CompleteReindexOutput>(
            typeof(CompleteReindexActivity),
            new CompleteReindexInput(
                input.JobId,
                input.TargetEventId,
                input.Targets,
                tenants,
                state.IgnoredLifecycleEvents));

        return new ReindexOrchestrationOutput(
            completed.Success,
            tenants,
            state.IgnoredLifecycleEvents
                .Concat(completed.IgnoredLifecycleEvents)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static async Task<TenantAdvance> AdvanceTenantAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexTenantState state)
    {
        if (state.IsCompleted)
        {
            return new TenantAdvance(state, 0);
        }

        try
        {
            if (state.Phase == "Barrier")
            {
                var cutoff = await context.ScheduleTask<RaiseBarrierOutput>(
                    typeof(RaiseBarrierActivity),
                    new RaiseBarrierInput(input.JobId, state.TenantId, input.TargetEventId));
                return new TenantAdvance(
                    state with
                    {
                        Phase = "Draining",
                        CutoffTransactionId = cutoff.CutoffTransactionId,
                        CutoffSurrogateId = cutoff.CutoffSurrogateId,
                        DrainStartedUtc = context.CurrentUtcDateTime
                    },
                    1);
            }

            if (state.Phase == "Draining")
            {
                var drain = await context.ScheduleTask<AwaitDrainOutput>(
                    typeof(AwaitDrainActivity),
                    new AwaitDrainInput(
                        input.JobId,
                        state.TenantId,
                        state.CutoffTransactionId,
                        state.DrainStartedUtc,
                        input.DrainWarningAfter ?? TimeSpan.FromMinutes(5)));
                if (!drain.IsDrained)
                {
                    await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(1), true);
                    return new TenantAdvance(state, 1);
                }

                return new TenantAdvance(state with { Phase = "Reindexing" }, 1);
            }

            if (state.ResourceTypeIndex >= input.ResourceTypes.Count)
            {
                return new TenantAdvance(state with { Phase = "Completed" }, 0);
            }

            var resourceType = input.ResourceTypes[state.ResourceTypeIndex];
            if (state.PendingRanges.Count == 0)
            {
                var plan = await context.ScheduleTask<PlanReindexOutput>(
                    typeof(PlanReindexActivity),
                    new PlanReindexInput(
                        input.JobId,
                        state.TenantId,
                        resourceType,
                        state.PlannerCursor,
                        state.CutoffSurrogateId,
                        input.Parameters.MaximumNumberOfResourcesPerQuery,
                        Math.Max(1, input.Parameters.MaximumConcurrency)));
                var planned = state with
                {
                    PendingRanges = plan.Ranges,
                    NextPlannerCursor = plan.NextStartAfter,
                    ResourcesToReindex = state.ResourcesToReindex +
                        plan.Ranges.Sum(range => range.ResourceCount)
                };
                if (plan.Ranges.Count == 0)
                {
                    planned = AdvancePlanner(planned);
                }

                return new TenantAdvance(planned, 1);
            }

            var wave = state.PendingRanges
                .Take(input.Parameters.MaximumConcurrency)
                .ToArray();
            var retry = new RetryOptions(TimeSpan.FromSeconds(1), 5)
            {
                BackoffCoefficient = 2,
                MaxRetryInterval = TimeSpan.FromSeconds(30)
            };
            var tasks = wave.Select(
                async range =>
                {
                    try
                    {
                        var output = await context.ScheduleWithRetry<ReindexRangeOutput>(
                            typeof(ReindexRangeActivity),
                            retry,
                            new ReindexRangeInput(
                                input.JobId,
                                state.TenantId,
                                resourceType,
                                range.Start,
                                range.End,
                                input.TargetEventId,
                                input.Parameters.MaximumNumberOfResourcesPerWrite,
                                input.Parameters.QueryDelayIntervalInMilliseconds));
                        return new RangeAttempt(output, null);
                    }
                    catch (Exception ex)
                    {
                        return new RangeAttempt(null, ex);
                    }
                });
            var attempts = await Task.WhenAll(tasks);
            var failures = state.FailedResources.ToList();
            var failedTypes = state.FailedResourceTypes.ToList();
            foreach (var attempt in attempts)
            {
                if (attempt.Output is not null)
                {
                    failures.AddRange(attempt.Output.FailedResources.Take(100 - failures.Count));
                }
                else
                {
                    if (!failedTypes.Contains(resourceType, StringComparer.OrdinalIgnoreCase))
                    {
                        failedTypes.Add(resourceType);
                    }

                    if (failures.Count < 100)
                    {
                        failures.Add(new ReindexFailedResource(
                            resourceType,
                            string.Empty,
                            attempt.Error!.Message));
                    }
                }
            }

            var advanced = state with
            {
                PendingRanges = state.PendingRanges.Skip(wave.Length).ToArray(),
                ResourcesRead = state.ResourcesRead +
                    attempts.Where(attempt => attempt.Output is not null)
                        .Sum(attempt => attempt.Output!.ResourcesRead),
                ResourcesReindexed = state.ResourcesReindexed +
                    attempts.Where(attempt => attempt.Output is not null)
                        .Sum(attempt => attempt.Output!.ResourcesReindexed),
                Conflicts = state.Conflicts +
                    attempts.Where(attempt => attempt.Output is not null)
                        .Sum(attempt => attempt.Output!.Conflicts),
                FailedResources = failures,
                FailedResourceTypes = failedTypes
            };
            if (advanced.PendingRanges.Count == 0)
            {
                advanced = AdvancePlanner(advanced);
            }

            return new TenantAdvance(advanced, wave.Length);
        }
        catch (Exception ex)
        {
            return new TenantAdvance(
                state with
                {
                    Phase = "Completed",
                    FailedResourceTypes = input.ResourceTypes
                        .Skip(state.ResourceTypeIndex)
                        .Concat(state.FailedResourceTypes)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    ErrorMessage = ex.Message
                },
                1);
        }
    }

    private static ReindexTenantState AdvancePlanner(ReindexTenantState state) =>
        state.NextPlannerCursor.HasValue
            ? state with
            {
                PlannerCursor = state.NextPlannerCursor.Value,
                NextPlannerCursor = null
            }
            : state with
            {
                ResourceTypeIndex = state.ResourceTypeIndex + 1,
                PlannerCursor = -1,
                NextPlannerCursor = null
            };

    private static void ContinueIfNeeded(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexOrchestrationState state,
        int scheduledActivities)
    {
        if (scheduledActivities < Math.Max(1, input.ContinueAsNewThreshold) ||
            state.Tenants.All(tenant => tenant.IsCompleted))
        {
            return;
        }

        context.ContinueAsNew(input with { State = state });
    }

    private sealed record TenantAdvance(ReindexTenantState State, int ScheduledActivities);

    private sealed record RangeAttempt(ReindexRangeOutput? Output, Exception? Error);
}
