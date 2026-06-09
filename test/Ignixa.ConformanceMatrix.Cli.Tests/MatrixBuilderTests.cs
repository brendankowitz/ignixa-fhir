using Shouldly;
using Ignixa.ConformanceMatrix.Cli.Reporting;

namespace Ignixa.ConformanceMatrix.Cli.Tests;

public class MatrixBuilderTests
{
    private static ImplReport MakeReport(
        string impl,
        DateTimeOffset? startedAt = null,
        long durationMs = 100,
        IReadOnlyList<ImplReportResult>? results = null) =>
        new()
        {
            Impl = impl,
            StartedAt = startedAt ?? DateTimeOffset.UtcNow,
            DurationMs = durationMs,
            Results = results ?? []
        };

    private static ImplReportResult MakeResult(
        string id,
        string status,
        string file = "module/test.json") =>
        new()
        {
            Id = id,
            File = file,
            Status = status,
            DurationMs = 10
        };

    [Fact]
    public void GivenPassFailSkippedResults_WhenCountingOutcomes_ThenCountsAreCorrect()
    {
        // Arrange
        var reports = new List<ImplReport>
        {
            MakeReport("implA", results:
            [
                MakeResult("test1", "pass"),
                MakeResult("test2", "fail"),
                MakeResult("test3", "skipped")
            ])
        };

        // Act
        var (pass, fail, skipped) = MatrixBuilder.CountOutcomes(reports);

        // Assert
        pass.ShouldBe(1);
        fail.ShouldBe(1);
        skipped.ShouldBe(1);
    }

    [Fact]
    public void GivenErrorStatusResults_WhenCountingOutcomes_ThenErrorCountsAsFail()
    {
        // Arrange
        var reports = new List<ImplReport>
        {
            MakeReport("implA", results:
            [
                MakeResult("test1", "error"),
                MakeResult("test2", "pass")
            ])
        };

        // Act
        var (pass, fail, skipped) = MatrixBuilder.CountOutcomes(reports);

        // Assert
        pass.ShouldBe(1);
        fail.ShouldBe(1);
        skipped.ShouldBe(0);
    }

    [Fact]
    public void GivenUnknownStatusResults_WhenCountingOutcomes_ThenUnknownCountsAsFail()
    {
        // Arrange
        var reports = new List<ImplReport>
        {
            MakeReport("implA", results:
            [
                MakeResult("test1", "unknown-status"),
                MakeResult("test2", "pass"),
                MakeResult("test3", "skipped")
            ])
        };

        // Act
        var (pass, fail, skipped) = MatrixBuilder.CountOutcomes(reports);

        // Assert
        pass.ShouldBe(1);
        fail.ShouldBe(1);
        skipped.ShouldBe(1);
    }

    [Fact]
    public void GivenForwardSlashFilePath_WhenExtractingModule_ThenFirstSegmentReturned()
    {
        // Act
        var modId = MatrixBuilder.ModuleIdFromFile("patient/crud.json");

        // Assert
        modId.ShouldBe("patient");
    }

    [Fact]
    public void GivenBackslashFilePath_WhenExtractingModule_ThenFirstSegmentReturned()
    {
        // Act
        var modId = MatrixBuilder.ModuleIdFromFile("patient\\crud.json");

        // Assert
        modId.ShouldBe("patient");
    }

    [Fact]
    public void GivenFileWithNoSlash_WhenExtractingModule_ThenWholeFileNameReturned()
    {
        // Act
        var modId = MatrixBuilder.ModuleIdFromFile("standalone.json");

        // Assert
        modId.ShouldBe("standalone.json");
    }

    [Fact]
    public void GivenMultipleImplReports_WhenMerged_ThenBothImplsAppearInRun()
    {
        // Arrange
        var t = DateTimeOffset.UtcNow;
        var reports = new List<ImplReport>
        {
            MakeReport("implA", startedAt: t),
            MakeReport("implB", startedAt: t.AddMilliseconds(50))
        };

        // Act
        var (run, _) = MatrixBuilder.MergeReports(reports);

        // Assert
        run.Impls.Select(i => i.Id).ShouldBe(["implA", "implB"], ignoreOrder: false);
    }

    [Fact]
    public void GivenResultsAcrossModules_WhenMerged_ThenModulesBucketedByFirstPathSegment()
    {
        // Arrange
        var reports = new List<ImplReport>
        {
            MakeReport("implA", results:
            [
                MakeResult("tc1", "pass", "patient/create.json"),
                MakeResult("tc2", "pass", "observation/read.json")
            ])
        };

        // Act
        var (run, _) = MatrixBuilder.MergeReports(reports);

        // Assert
        run.Modules.Select(m => m.Id).ShouldBe(["observation", "patient"], ignoreOrder: false);
    }

    [Fact]
    public void GivenStartedAtAcrossImplReports_WhenMerged_ThenEarliestStartUsedForRunId()
    {
        // Arrange
        var early = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero);
        var late = early.AddMinutes(5);
        var reports = new List<ImplReport>
        {
            MakeReport("implA", startedAt: late),
            MakeReport("implB", startedAt: early)
        };

        // Act
        var (run, index) = MatrixBuilder.MergeReports(reports);

        // Assert
        run.Meta.Id.ShouldBe("run-2024-03-15-103000");
        run.Meta.StartedAt.ShouldBe(early);
        index.StartedAt.ShouldBe(early);
    }

    [Fact]
    public void GivenMixedOutcomes_WhenMerged_ThenIndexCountsAreAccurate()
    {
        // Arrange
        var reports = new List<ImplReport>
        {
            MakeReport("implA", results:
            [
                MakeResult("t1", "pass"),
                MakeResult("t2", "fail"),
                MakeResult("t3", "error"),
                MakeResult("t4", "skipped")
            ])
        };

        // Act
        var (_, index) = MatrixBuilder.MergeReports(reports);

        // Assert
        index.Pass.ShouldBe(1);
        index.Fail.ShouldBe(2);
        index.Skipped.ShouldBe(1);
    }

    [Fact]
    public void GivenEmptyReports_WhenMergeReportsCalled_ThenRunMetaHasEmptyImpls()
    {
        // Arrange
        var reports = new List<ImplReport>();

        // Act
        var (run, index) = MatrixBuilder.MergeReports(reports);

        // Assert
        run.Impls.ShouldBeEmpty();
        index.Pass.ShouldBe(0);
        index.Fail.ShouldBe(0);
        index.Skipped.ShouldBe(0);
    }

    [Fact]
    public void GivenBackslashInFilePaths_WhenBuildingModules_ThenBucketedCorrectly()
    {
        // Arrange
        var reports = new List<ImplReport>
        {
            MakeReport("implA", results:
            [
                MakeResult("tc1", "pass", "patient\\create.json"),
                MakeResult("tc2", "fail", "patient\\update.json")
            ])
        };

        // Act
        var (run, _) = MatrixBuilder.MergeReports(reports);

        // Assert
        run.Modules.ShouldHaveSingleItem();
        run.Modules[0].Id.ShouldBe("patient");
    }

    [Fact]
    public void GivenIsFail_WhenPassStatusChecked_ThenReturnsFalse()
    {
        MatrixBuilder.IsFail("pass").ShouldBeFalse();
        MatrixBuilder.IsFail("skipped").ShouldBeFalse();
        MatrixBuilder.IsFail("fail").ShouldBeTrue();
        MatrixBuilder.IsFail("error").ShouldBeTrue();
        MatrixBuilder.IsFail("unknown").ShouldBeTrue();
    }
}
