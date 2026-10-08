using System.Diagnostics.Metrics;

namespace Ignixa.Application.Features.Conformance;

public static class ReindexReconciliationMetrics
{
    private static readonly Meter Meter = new("Ignixa.Reindex");
    private static readonly Counter<long> Failures =
        Meter.CreateCounter<long>("reindex.reconciliation.failures");

    public static void RecordFailure() => Failures.Add(1);
}
