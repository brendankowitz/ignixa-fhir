using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Schedules durable phase-two transitions that were still pending when this process started.
/// </summary>
public sealed class SearchParameterTransitionReconciler(
    ConformanceState conformanceState,
    ISearchParameterTransitionScheduler transitionScheduler,
    IOptions<ConformanceTransitionOptions> transitionOptions)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        foreach (var hideEventId in conformanceState.GetTransitionHideEventIds())
        {
            // Do not derive elapsed grace from the source-event timestamp: it is not the
            // database commit time. A replacement durable instance always waits full grace.
            await transitionScheduler.ScheduleAsync(
                hideEventId,
                transitionOptions.Value.TransitionGrace,
                cancellationToken);
        }
    }
}
