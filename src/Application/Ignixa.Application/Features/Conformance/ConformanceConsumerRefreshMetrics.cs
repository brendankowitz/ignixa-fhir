using System.Diagnostics.Metrics;

namespace Ignixa.Application.Features.Conformance;

internal static class ConformanceConsumerRefreshMetrics
{
    private static readonly Meter Meter = new("Ignixa.Conformance");
    private static readonly Counter<long> FailureCounter = Meter.CreateCounter<long>("conformance.consumer_refresh.failures");

    public static void RecordFailure(string origin) =>
        FailureCounter.Add(1, new KeyValuePair<string, object?>("origin", origin));
}
