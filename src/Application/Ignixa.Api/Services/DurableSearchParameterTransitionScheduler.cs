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
        try
        {
            await taskHubClient.CreateOrchestrationInstanceAsync(
                typeof(SearchParameterTransitionOrchestration),
                GetInstanceId(hideEventId),
                new SearchParameterTransitionOrchestrationInput(hideEventId, transitionGrace),
                dedupeStatuses: ActiveStatuses);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // The deterministic instance id makes activation retries and reconciliation idempotent.
        }
    }

    public static string GetInstanceId(long hideEventId) => $"search-parameter-transition-{hideEventId}";
}
