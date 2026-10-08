using System.Diagnostics.Metrics;

namespace Ignixa.Application.Features.Conformance;

public static class ReindexTriggerMetrics
{
    private static readonly Meter Meter = new("Ignixa.Reindex");
    private static readonly Counter<long> Failures =
        Meter.CreateCounter<long>("reindex.trigger.failures");

    public static void RecordFailure(string trigger) =>
        Failures.Add(1, new KeyValuePair<string, object?>("trigger", trigger));
}
