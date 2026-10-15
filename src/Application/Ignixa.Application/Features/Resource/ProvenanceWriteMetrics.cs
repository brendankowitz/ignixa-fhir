using System.Diagnostics.Metrics;

namespace Ignixa.Application.Features.Resource;

internal static class ProvenanceWriteMetrics
{
    private static readonly Meter Meter = new("Ignixa.Provenance");
    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>("provenance.write.failures");

    public static void RecordFailure() => FailureCounter.Add(1);
}
