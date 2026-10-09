using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Conformance;
using Ignixa.Application.Features.Conformance;

namespace Ignixa.Api.Services;

/// <summary>
/// Creates the durable phase-two transition orchestration for one hide event.
/// </summary>
/// <remarks>
/// The instance id is derived only from the hide event id, so activation and every node's startup
/// reconciliation collapse onto one active orchestration. A terminal predecessor is replaced by a new
/// full-grace execution.
/// </remarks>
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
            // An active orchestration for this hide event already waits its grace and will commit it.
        }
    }

    public static string GetInstanceId(long hideEventId) => $"search-parameter-transition-{hideEventId}";
}
