using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexProgressTests
{
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

    [Fact]
    public void GivenProgressWithFailures_WhenRoundTrippedThroughJson_ThenTotalsAndSamplesDerive()
    {
        var progress = new ReindexProgress(ReindexPhase.Completing)
        {
            CancellationReason = "operator",
            IgnoredLifecycleEvents = ["http://example.org/ignored"],
            Tenants =
            [
                new ReindexTenantProgress(1, ReindexTenantStatus.Completed, 10, 20, 5, 5, 5, 1, 0, null),
                new ReindexTenantProgress(2, ReindexTenantStatus.Failed, 11, 21, 5, 5, 4, 2, 1, "one failed")
                {
                    FailedResources = [new ReindexFailedResource("Patient", "p1", "boom")],
                    FailedResourceTypes = ["Patient"]
                }
            ]
        };

        var json = progress.ToJson();
        var restored = ReindexProgress.FromJson(json)!;

        json["phase"]!.GetValue<string>().ShouldBe("Completing");
        json["tenants"]![1]!["status"]!.GetValue<string>().ShouldBe("Failed");
        restored.ToJson().ToJsonString().ShouldBe(json.ToJsonString());
        restored.TotalResourcesToReindex.ShouldBe(10);
        restored.ResourcesSuccessfullyReindexed.ShouldBe(9);
        restored.Conflicts.ShouldBe(3);
        restored.FailedResources.ShouldHaveSingleItem().Id.ShouldBe("p1");
        restored.Tenants[0].Success.ShouldBeTrue();
        restored.Tenants[1].Success.ShouldBeFalse();
    }
}
