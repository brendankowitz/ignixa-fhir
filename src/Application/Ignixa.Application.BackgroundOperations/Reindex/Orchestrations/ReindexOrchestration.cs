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

        if (!state.DebounceCompleted)
        {
            if (input.StartDebounce > TimeSpan.Zero)
            {
                await context.CreateTimer(
                    context.CurrentUtcDateTime.Add(input.StartDebounce),
                    true);
            }

            state = state with { DebounceCompleted = true };
        }

        if (!state.Started)
        {
            var retry = CreateRetryOptions();
            StartReindexOutput started;
            try
            {
                started = await context.ScheduleWithRetry<StartReindexOutput>(
                    typeof(StartReindexActivity),
                    retry,
                    new StartReindexInput(input.JobId, input.TargetEventId, input.Targets, input.TenantIds));
            }
            catch (Exception ex)
            {
                return await CompleteFailureAsync(context, input, state, ex);
            }

            scheduledActivities++;
            state = state with
            {
                Started = true,
                IgnoredLifecycleEvents = started.IgnoredLifecycleEvents
            };
            if (started.TargetEventId.HasValue &&
                started.ResourceTypes is not null &&
                started.Targets is not null)
            {
                input = input with
                {
                    TargetEventId = started.TargetEventId.Value,
                    ResourceTypes = started.ResourceTypes,
                    Targets = started.Targets
                };
            }
            if (ContinueIfNeeded(context, input, state, scheduledActivities))
            {
                return default!;
            }
        }

        if (!state.BarrierDelayCompleted)
        {
            try
            {
                var remaining = input.BarrierDelay > input.StartDebounce
                    ? input.BarrierDelay - input.StartDebounce
                    : TimeSpan.Zero;
                do
                {
                    var delay = remaining > input.HeartbeatInterval ? input.HeartbeatInterval : remaining;
                    await context.CreateTimer(context.CurrentUtcDateTime.Add(delay), true);
                    remaining -= delay;
                    if (remaining > TimeSpan.Zero)
                    {
                        state = await PersistProgressAsync(context, input, state, "BarrierDelay");
                        scheduledActivities++;
                    }
                }
                while (remaining > TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                return await CompleteFailureAsync(context, input, state, ex);
            }

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
            var phase = state.Tenants.All(tenant => tenant.IsCompleted)
                ? "Completing"
                : state.Tenants.Any(tenant => tenant.Phase == "Reindexing")
                    ? "Reindexing"
                    : "Draining";
            // Persist outside the tenant/range failure boundary: retrying this activity never repeats range work.
            state = await PersistProgressAsync(context, input, state, phase);
            scheduledActivities++;
            if (ContinueIfNeeded(context, input, state, scheduledActivities))
            {
                return default!;
            }
        }

        var tenants = state.Tenants.Select(tenant => tenant.ToOutput()).ToArray();

        var completed = await context.ScheduleWithRetry<CompleteReindexOutput>(
            typeof(CompleteReindexActivity),
            CreateRetryOptions(),
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

    private static async Task<ReindexOrchestrationOutput> CompleteFailureAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexOrchestrationState state,
        Exception error)
    {
        var tenants = state.Tenants.Select(tenant => tenant.ToOutput()).ToArray();
        var completed = await context.ScheduleWithRetry<CompleteReindexOutput>(
            typeof(CompleteReindexActivity),
            CreateRetryOptions(),
            new CompleteReindexInput(
                input.JobId,
                input.TargetEventId,
                input.Targets,
                tenants,
                state.IgnoredLifecycleEvents)
            {
                FailureMessage = $"Reindex orchestration failed: {error.Message}"
            });
        return new ReindexOrchestrationOutput(
            completed.Success,
            tenants,
            state.IgnoredLifecycleEvents
                .Concat(completed.IgnoredLifecycleEvents)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static RetryOptions CreateRetryOptions() =>
        new(TimeSpan.FromSeconds(1), 5)
        {
            BackoffCoefficient = 2,
            MaxRetryInterval = TimeSpan.FromSeconds(30)
        };

    private static async Task<ReindexOrchestrationState> PersistProgressAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexOrchestrationState state,
        string phase)
    {
        var next = state with { ProgressSequence = state.ProgressSequence + 1 };
        await context.ScheduleWithRetry<bool>(
            typeof(PersistReindexProgressActivity),
            new RetryOptions(TimeSpan.FromSeconds(1), int.MaxValue)
            {
                BackoffCoefficient = 2,
                MaxRetryInterval = TimeSpan.FromSeconds(30)
            },
            new PersistReindexProgressInput(input.JobId, next.ProgressSequence, phase, next.Tenants));
        return next;
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
                    return new TenantAdvance(state with { VisibleWatermark = drain.VisibleWatermark }, 1);
                }

                return new TenantAdvance(
                    state with { Phase = "Reindexing", VisibleWatermark = drain.VisibleWatermark }, 1);
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
            var retry = CreateRetryOptions();
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
                    foreach (var failedType in attempt.Output.FailedResourceTypes)
                    {
                        if (!failedTypes.Contains(failedType, StringComparer.OrdinalIgnoreCase))
                        {
                            failedTypes.Add(failedType);
                        }
                    }
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
                FailedResourceCount = state.FailedResourceCount +
                    attempts.Where(attempt => attempt.Output is not null)
                        .Sum(attempt => attempt.Output!.FailedResourceCount),
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

    private static bool ContinueIfNeeded(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexOrchestrationState state,
        int scheduledActivities)
    {
        if (scheduledActivities < Math.Max(1, input.ContinueAsNewThreshold) ||
            state.Tenants.All(tenant => tenant.IsCompleted))
        {
            return false;
        }

        context.ContinueAsNew(input with { State = state });
        return true;
    }

    private sealed record TenantAdvance(ReindexTenantState State, int ScheduledActivities);

    private sealed record RangeAttempt(ReindexRangeOutput? Output, Exception? Error);
}
