using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Conformance;
using Ignixa.Application.Features.Conformance;

namespace Ignixa.Api.Services;

/// <summary>
/// Creates the durable phase-two transition orchestration for one hide event.
/// </summary>
public sealed class DurableSearchParameterTransitionScheduler(TaskHubClient taskHubClient)
    : ISearchParameterTransitionScheduler
{
    private static readonly OrchestrationStatus[] ActiveStatuses =
    [
        OrchestrationStatus.Running,
        OrchestrationStatus.Pending,
        OrchestrationStatus.ContinuedAsNew,
    ];

    public async Task ScheduleAsync(
        long hideEventId,
        TimeSpan transitionGrace,
        CancellationToken cancellationToken)
    {
        await ScheduleAsync(
            hideEventId,
            transitionGrace,
            GetInstanceId(hideEventId),
            cancellationToken);
    }

    public async Task ScheduleReconciliationAsync(
        long hideEventId,
        TimeSpan transitionGrace,
        CancellationToken cancellationToken)
    {
        await ScheduleAsync(
            hideEventId,
            transitionGrace,
            $"{GetInstanceId(hideEventId)}-r-{Guid.NewGuid():N}",
            cancellationToken);
    }

    private async Task ScheduleAsync(
        long hideEventId,
        TimeSpan transitionGrace,
        string instanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await taskHubClient.CreateOrchestrationInstanceAsync(
                typeof(SearchParameterTransitionOrchestration),
                instanceId,
                new SearchParameterTransitionOrchestrationInput(hideEventId, transitionGrace),
                dedupeStatuses: ActiveStatuses);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // The deterministic activation instance id makes activation retries idempotent.
        }
    }

    public static string GetInstanceId(long hideEventId) => $"search-parameter-transition-{hideEventId}";
}
