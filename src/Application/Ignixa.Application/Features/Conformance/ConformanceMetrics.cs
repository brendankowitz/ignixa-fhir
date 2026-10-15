using System.Diagnostics.Metrics;

namespace Ignixa.Application.Features.Conformance;

internal static class ConformanceMetrics
{
    private static readonly Meter Meter = new("Ignixa.Conformance");
    private static readonly Counter<long> BarrierRejections =
        Meter.CreateCounter<long>("conformance.barrier.rejections");
    private static readonly Counter<long> ConsumerRefreshFailures =
        Meter.CreateCounter<long>("conformance.consumer_refresh.failures");
    private static readonly Counter<long> TransitionScheduleFailures =
        Meter.CreateCounter<long>("conformance.transition.schedule_failures");
    private static readonly Counter<long> LeaseLost =
        Meter.CreateCounter<long>("conformance.lease.lost");

    public static void RecordBarrierRejection(string outcome) =>
        BarrierRejections.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordConsumerRefreshFailure(string origin) =>
        ConsumerRefreshFailures.Add(1, new KeyValuePair<string, object?>("origin", origin));

    public static void RecordTransitionScheduleFailure() => TransitionScheduleFailures.Add(1);

    public static void RecordLeaseLost() => LeaseLost.Add(1);

    public static ObservableGauge<double> CreateLeaseAgeGauge(Func<double> observeValue) =>
        Meter.CreateObservableGauge("conformance.lease.age", observeValue, unit: "s");
}
