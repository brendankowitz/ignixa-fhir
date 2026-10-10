using System.Diagnostics.Metrics;

namespace Ignixa.Application.Features.Conformance;

public static class ReindexMetrics
{
    private static readonly Meter Meter = new("Ignixa.Reindex");
    private static readonly Counter<long> ResourcesProcessed =
        Meter.CreateCounter<long>("reindex.resources.processed");
    private static readonly Counter<long> Conflicts =
        Meter.CreateCounter<long>("reindex.resources.conflicts");
    private static readonly Counter<long> Failed =
        Meter.CreateCounter<long>("reindex.resources.failed");
    private static readonly UpDownCounter<long> ActiveRanges =
        Meter.CreateUpDownCounter<long>("reindex.ranges.active");
    private static readonly Histogram<double> DrainWait =
        Meter.CreateHistogram<double>("reindex.drain.wait", "s");
    private static readonly Histogram<double> JobDuration =
        Meter.CreateHistogram<double>("reindex.job.duration", "s");
    private static readonly Counter<long> ProgressFailures =
        Meter.CreateCounter<long>("reindex.progress.persistence_failures");
    private static readonly Counter<long> Triggers =
        Meter.CreateCounter<long>("reindex.triggers");
    private static readonly Counter<long> ReconciliationFailures =
        Meter.CreateCounter<long>("reindex.reconciliation.failures");
    private static readonly Counter<long> TriggerFailures =
        Meter.CreateCounter<long>("reindex.trigger.failures");

    public static void ProgressPersistenceFailed() => ProgressFailures.Add(1);

    public static void TriggerStarted(string trigger) =>
        Triggers.Add(1, new KeyValuePair<string, object?>("trigger", trigger));

    public static void RangeStarted() => ActiveRanges.Add(1);

    public static void RangeCompleted(long resourcesReindexed, long conflicts, long failedResources)
    {
        ActiveRanges.Add(-1);
        ResourcesProcessed.Add(resourcesReindexed);
        Conflicts.Add(conflicts);
        Failed.Add(failedResources);
    }

    public static void RangeFailed() => ActiveRanges.Add(-1);

    public static void RecordDrainWait(TimeSpan elapsed) =>
        DrainWait.Record(elapsed.TotalSeconds);

    public static void RecordJobDuration(TimeSpan elapsed) =>
        JobDuration.Record(elapsed.TotalSeconds);

    public static void RecordReconciliationFailure() => ReconciliationFailures.Add(1);

    public static void RecordTriggerFailure(string trigger) =>
        TriggerFailures.Add(1, new KeyValuePair<string, object?>("trigger", trigger));
}
