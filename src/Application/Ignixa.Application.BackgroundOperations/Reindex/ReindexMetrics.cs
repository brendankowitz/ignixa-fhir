using System.Diagnostics.Metrics;
using Ignixa.Application.BackgroundOperations.Reindex.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

internal static class ReindexMetrics
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

    public static void ProgressPersistenceFailed() => ProgressFailures.Add(1);

    public static void RangeStarted() => ActiveRanges.Add(1);

    public static void RangeCompleted(ReindexRangeOutput output)
    {
        ActiveRanges.Add(-1);
        ResourcesProcessed.Add(output.ResourcesReindexed);
        Conflicts.Add(output.Conflicts);
        Failed.Add(output.FailedResources.Count);
    }

    public static void RangeFailed() => ActiveRanges.Add(-1);

    public static void RecordDrainWait(TimeSpan elapsed) =>
        DrainWait.Record(elapsed.TotalSeconds);

    public static void RecordJobDuration(TimeSpan elapsed) =>
        JobDuration.Record(elapsed.TotalSeconds);
}
