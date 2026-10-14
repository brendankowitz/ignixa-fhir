using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexProgressTests
{
    // §6.3: progress is capped at 99.9 for every status other than Completed.
    [Theory]
    [InlineData(ReindexJobStatus.Running, 10, 10, 99.9)]
    [InlineData(ReindexJobStatus.Failed, 10, 10, 99.9)]
    [InlineData(ReindexJobStatus.Running, 10, 7, 70)]
    [InlineData(ReindexJobStatus.Running, 0, 0, 0)]
    [InlineData(ReindexJobStatus.Completed, 0, 0, 100)]
    [InlineData(ReindexJobStatus.Completed, 10, 10, 100)]
    public void GivenCounts_WhenPercentIsReported_ThenOnlyACompletedJobReachesOneHundred(
        ReindexJobStatus status,
        long total,
        long reindexed,
        double expected)
    {
        var progress = new ReindexProgress(ReindexPhase.Reindexing)
        {
            Tenants =
            [
                ReindexTenantProgress.Create(1) with
                {
                    ResourcesToReindex = total,
                    ResourcesReindexed = reindexed
                }
            ]
        };

        progress.PercentComplete(status).ShouldBe(expected);
    }
}
