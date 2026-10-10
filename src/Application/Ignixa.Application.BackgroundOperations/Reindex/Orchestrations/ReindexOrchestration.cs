using DurableTask.Core;
using DurableTask.Core.Exceptions;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;

/// <summary>
/// Coordinates one reindex job: start, barrier delay, then per tenant the barrier, the drain and the range
/// waves, and finally completion. Progress is persisted at every phase boundary and every
/// <see cref="WavesPerProgressSnapshot"/> range waves; a persist that finds the job closed stops the
/// orchestration without completing it. Tenants that wait on a polled condition do so on one durable timer
/// while the others keep working.
/// </summary>
public sealed class ReindexOrchestration
    : TaskOrchestration<ReindexOrchestrationOutput, ReindexOrchestrationInput>
{
    internal const int WavesPerProgressSnapshot = 5;

    public override async Task<ReindexOrchestrationOutput> RunTask(
        OrchestrationContext context,
        ReindexOrchestrationInput input)
    {
        var state = input.State ?? ReindexOrchestrationState.Create(input.TenantIds);
        var scheduledActivities = 0;

        if (!state.Started)
        {
            StartReindexOutput started;
            try
            {
                started = await context.ScheduleWithRetry<StartReindexOutput>(
                    typeof(StartReindexActivity),
                    CreateRetryOptions(),
                    new StartReindexInput(input.JobId, input.TargetEventId, input.Targets, input.TenantIds));
            }
            catch (Exception ex)
            {
                return await CompleteFailureAsync(context, input, state, ex);
            }

            scheduledActivities++;
            if (!started.ShouldContinue)
            {
                return Stopped(state);
            }

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

        if (state.Phase == ReindexPhase.BarrierDelay)
        {
            try
            {
                // A long delay is split so the job's heartbeat stays fresher than the stale-job timeout.
                var remaining = input.BarrierDelay;
                var longestWait = input.StaleJobTimeout / 2;
                do
                {
                    var delay = remaining > longestWait ? longestWait : remaining;
                    await context.CreateTimer(context.CurrentUtcDateTime.Add(delay), true);
                    remaining -= delay;
                    if (remaining > TimeSpan.Zero)
                    {
                        if (!await PersistProgressAsync(context, input, state))
                        {
                            return Stopped(state);
                        }

                        scheduledActivities++;
                    }
                }
                while (remaining > TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                return await CompleteFailureAsync(context, input, state, ex);
            }

            state = state with { Phase = ReindexPhase.Draining };
            if (!await PersistProgressAsync(context, input, state))
            {
                return Stopped(state);
            }

            scheduledActivities++;
            if (ContinueIfNeeded(context, input, state, scheduledActivities))
            {
                return default!;
            }
        }

        while (!state.AllTenantsCompleted)
        {
            var now = context.CurrentUtcDateTime;
            var advances = await Task.WhenAll(state.Tenants.Select(
                tenant => AdvanceTenantAsync(context, input, tenant, now)));
            scheduledActivities += advances.Sum(advance => advance.ScheduledActivities);
            var previousPhase = state.Phase;
            state = state with
            {
                Tenants = advances.Select(advance => advance.State).ToArray(),
                WavesSinceSnapshot = state.WavesSinceSnapshot + (advances.Any(advance => advance.IsWave) ? 1 : 0)
            };
            state = state with { Phase = DerivePhase(state) };
            if (state.Phase != previousPhase || state.WavesSinceSnapshot >= WavesPerProgressSnapshot)
            {
                // Persist outside the tenant/range failure boundary: retrying this activity never repeats range work.
                if (!await PersistProgressAsync(context, input, state))
                {
                    return Stopped(state);
                }

                scheduledActivities++;
                state = state with { WavesSinceSnapshot = 0 };
            }

            if (ContinueIfNeeded(context, input, state, scheduledActivities))
            {
                return default!;
            }

            await WaitForNextPollAsync(context, state);
        }

        var tenants = state.Tenants.Select(tenant => tenant.Progress).ToArray();

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
        var tenants = state.Tenants.Select(tenant => tenant.Progress).ToArray();
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

    /// <summary>The job row was finished or removed by someone else; its terminal state is authoritative.</summary>
    private static ReindexOrchestrationOutput Stopped(ReindexOrchestrationState state) =>
        new(
            false,
            state.Tenants.Select(tenant => tenant.Progress).ToArray(),
            state.IgnoredLifecycleEvents);

    private static RetryOptions CreateRetryOptions() =>
        new(TimeSpan.FromSeconds(1), 5)
        {
            BackoffCoefficient = 2,
            MaxRetryInterval = TimeSpan.FromSeconds(30)
        };

    // Progress storage gets a longer, still bounded, window than range work: a snapshot that cannot be
    // written after this fails the job visibly instead of retrying forever.
    private static RetryOptions CreateProgressRetryOptions() =>
        new(TimeSpan.FromSeconds(1), 8)
        {
            BackoffCoefficient = 2,
            MaxRetryInterval = TimeSpan.FromSeconds(30)
        };

    private static Task<bool> PersistProgressAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexOrchestrationState state) =>
        context.ScheduleWithRetry<bool>(
            typeof(PersistReindexProgressActivity),
            CreateProgressRetryOptions(),
            new PersistReindexProgressInput(input.JobId, state.ToProgress()));

    private static ReindexPhase DerivePhase(ReindexOrchestrationState state)
    {
        var derived = state.AllTenantsCompleted
            ? ReindexPhase.Completing
            : state.Tenants.Any(tenant => tenant.Status == ReindexTenantStatus.Reindexing)
                ? ReindexPhase.Reindexing
                : ReindexPhase.Draining;
        return derived > state.Phase ? derived : state.Phase;
    }

    /// <summary>
    /// When every unfinished tenant is waiting on a poll that is not yet due, sleeps on one durable timer
    /// until the earliest of them is.
    /// </summary>
    private static async Task WaitForNextPollAsync(OrchestrationContext context, ReindexOrchestrationState state)
    {
        var now = context.CurrentUtcDateTime;
        var waiting = state.Tenants.Where(tenant => !tenant.IsCompleted).ToArray();
        if (waiting.Length == 0 || waiting.Any(tenant => tenant.Wait is null || tenant.Wait.IsDue(now)))
        {
            return;
        }

        await context.CreateTimer(waiting.Min(tenant => tenant.Wait!.NextAttemptUtc), true);
    }

    private static async Task<TenantAdvance> AdvanceTenantAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexTenantState state,
        DateTime now)
    {
        if (state.IsCompleted || state.Wait is { } wait && !wait.IsDue(now))
        {
            return new TenantAdvance(state, 0, false);
        }

        try
        {
            return state.Status switch
            {
                ReindexTenantStatus.BarrierDelay => await RaiseBarrierAsync(context, input, state),
                ReindexTenantStatus.Draining => await PollDrainAsync(context, input, state, now),
                ReindexTenantStatus.Reindexing => await ReindexAsync(context, input, state),
                _ => throw new InvalidOperationException($"Tenant {state.TenantId} cannot advance from {state.Status}.")
            };
        }
        catch (Exception ex)
        {
            return new TenantAdvance(Fail(state, input, ex.Message), 1, false);
        }
    }

    private static async Task<TenantAdvance> RaiseBarrierAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexTenantState state)
    {
        var cutoff = await context.ScheduleWithRetry<RaiseBarrierOutput>(
            typeof(RaiseBarrierActivity),
            CreateRetryOptions(),
            new RaiseBarrierInput(input.JobId, state.TenantId, input.TargetEventId));
        return new TenantAdvance(
            state with
            {
                Progress = state.Progress with
                {
                    Status = ReindexTenantStatus.Draining,
                    CutoffTransactionId = cutoff.CutoffTransactionId,
                    CutoffSurrogateId = cutoff.CutoffSurrogateId
                },
                Wait = ReindexWait.Begin(context.CurrentUtcDateTime)
            },
            1,
            false);
    }

    private static async Task<TenantAdvance> PollDrainAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexTenantState state,
        DateTime now)
    {
        var wait = state.Wait ?? ReindexWait.Begin(now);
        var drainElapsed = wait.Elapsed(now);
        var drain = await context.ScheduleWithRetry<AwaitDrainOutput>(
            typeof(AwaitDrainActivity),
            CreateRetryOptions(),
            new AwaitDrainInput(
                input.JobId,
                state.TenantId,
                state.RequireCutoff().TransactionId,
                wait.StartedUtc,
                input.DrainWarningAfter ?? TimeSpan.FromMinutes(5),
                drainElapsed,
                input.StaleJobTimeout));
        if (drain.IsDrained)
        {
            return new TenantAdvance(
                state with
                {
                    Progress = state.Progress with { Status = ReindexTenantStatus.Reindexing },
                    VisibleWatermark = drain.VisibleWatermark,
                    Wait = null
                },
                1,
                false);
        }

        if (drainElapsed >= input.StaleJobTimeout)
        {
            throw new InvalidOperationException(
                $"Reindex drain exceeded the stale job timeout of {input.StaleJobTimeout}.");
        }

        return new TenantAdvance(
            state with
            {
                VisibleWatermark = drain.VisibleWatermark,
                Wait = wait.Backoff(context.CurrentUtcDateTime)
            },
            1,
            false);
    }

    private static async Task<TenantAdvance> ReindexAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexTenantState state)
    {
        if (state.ResourceTypeIndex >= input.ResourceTypes.Count)
        {
            return new TenantAdvance(
                state with { Progress = state.Progress with { Status = ReindexTenantStatus.Completed } },
                0,
                false);
        }

        var resourceType = input.ResourceTypes[state.ResourceTypeIndex];
        if (state.PendingRanges.Count == 0)
        {
            var plan = await context.ScheduleWithRetry<PlanReindexOutput>(
                typeof(PlanReindexActivity),
                CreateRetryOptions(),
                new PlanReindexInput(
                    input.JobId,
                    state.TenantId,
                    resourceType,
                    state.PlannerCursor,
                    state.RequireCutoff().SurrogateId,
                    input.Parameters.MaximumNumberOfResourcesPerQuery,
                    Math.Max(1, input.Parameters.MaximumConcurrency)));
            var planned = state with
            {
                PendingRanges = plan.Ranges,
                NextPlannerCursor = plan.NextStartAfter,
                Progress = state.Progress with
                {
                    ResourcesToReindex = state.Progress.ResourcesToReindex +
                        plan.Ranges.Sum(range => range.ResourceCount)
                }
            };
            if (plan.Ranges.Count == 0)
            {
                planned = AdvancePlanner(planned);
            }

            return new TenantAdvance(planned, 1, false);
        }

        var wave = state.PendingRanges
            .Take(input.Parameters.MaximumConcurrency)
            .ToArray();
        var attempts = await Task.WhenAll(wave.Select(range => RunRangeAsync(context, input, state, resourceType, range)));
        var progress = state.Progress;
        foreach (var attempt in attempts)
        {
            progress = attempt.Output is not null
                ? progress.Add(attempt.Output)
                : progress.AddRangeFailure(resourceType, attempt.Error!.Message);
        }

        var advanced = state with
        {
            PendingRanges = state.PendingRanges.Skip(wave.Length).ToArray(),
            Progress = progress
        };
        if (advanced.PendingRanges.Count == 0)
        {
            advanced = AdvancePlanner(advanced);
        }

        return new TenantAdvance(advanced, wave.Length, true);
    }

    private static async Task<RangeAttempt> RunRangeAsync(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexTenantState state,
        string resourceType,
        ReindexRange range)
    {
        try
        {
            var output = await context.ScheduleWithRetry<ReindexRangeOutput>(
                typeof(ReindexRangeActivity),
                CreateRangeRetryOptions(input.StaleJobTimeout),
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
    }

    private static ReindexTenantState Fail(ReindexTenantState state, ReindexOrchestrationInput input, string message) =>
        state with
        {
            Progress = state.Progress.Fail(message, input.ResourceTypes.Skip(state.ResourceTypeIndex)),
            Wait = null
        };

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
                PlannerCursor = null,
                NextPlannerCursor = null
            };

    private static RetryOptions CreateRangeRetryOptions(TimeSpan retryTimeout)
    {
        var standardFailureCount = 0;
        return new RetryOptions(TimeSpan.FromSeconds(1), int.MaxValue)
        {
            BackoffCoefficient = 2,
            MaxRetryInterval = TimeSpan.FromSeconds(30),
            RetryTimeout = retryTimeout,
            Handle = error => IsDefinitionsNotReady(error) || ++standardFailureCount < 5
        };
    }

    private static bool IsDefinitionsNotReady(Exception error) =>
        error is ReindexDefinitionsNotReadyException ||
        error.InnerException is ReindexDefinitionsNotReadyException ||
        error is TaskFailedException { FailureDetails: { } details } &&
        details.IsCausedBy<ReindexDefinitionsNotReadyException>();

    private static bool ContinueIfNeeded(
        OrchestrationContext context,
        ReindexOrchestrationInput input,
        ReindexOrchestrationState state,
        int scheduledActivities)
    {
        if (scheduledActivities < Math.Max(1, input.ContinueAsNewThreshold) ||
            state.AllTenantsCompleted)
        {
            return false;
        }

        context.ContinueAsNew(input with { State = state });
        return true;
    }

    private sealed record TenantAdvance(ReindexTenantState State, int ScheduledActivities, bool IsWave);

    private sealed record RangeAttempt(ReindexRangeOutput? Output, Exception? Error);
}
