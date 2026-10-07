using System.Diagnostics.Metrics;

namespace Ignixa.Application.Features.Conformance;

internal static class ConformanceBarrierMetrics
{
    private static readonly Meter Meter = new("Ignixa.Conformance");
    private static readonly Counter<long> RejectionCounter =
        Meter.CreateCounter<long>("conformance.barrier.rejections");

    public static void RecordRejection(string outcome) =>
        RejectionCounter.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
