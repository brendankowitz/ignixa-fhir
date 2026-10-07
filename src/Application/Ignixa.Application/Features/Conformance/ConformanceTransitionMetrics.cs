using System.Diagnostics.Metrics;

namespace Ignixa.Application.Features.Conformance;

internal static class ConformanceTransitionMetrics
{
    private static readonly Meter Meter = new("Ignixa.Conformance");
    private static readonly Counter<long> ScheduleFailureCounter = Meter.CreateCounter<long>("conformance.transition.schedule_failures");

    public static void RecordScheduleFailure() => ScheduleFailureCounter.Add(1);
}
