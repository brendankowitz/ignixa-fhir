using Shouldly;
using Ignixa.ConformanceMatrix.Cli.Commands;
using Ignixa.ConformanceMatrix.Cli.Reporting;

namespace Ignixa.ConformanceMatrix.Cli.Tests;

public class ExitCodeTests
{
    private static ImplReportResult MakeResult(string status) =>
        new()
        {
            Id = "test",
            File = "test.json",
            Status = status,
            DurationMs = 0
        };

    [Fact]
    public void GivenAllPassResults_WhenClassifyingExitCode_ThenReturnsZero()
    {
        // Arrange
        var results = new List<ImplReportResult>
        {
            MakeResult("pass"),
            MakeResult("pass")
        };

        // Act
        var code = RunCommand.ClassifyExitCode(results);

        // Assert
        code.ShouldBe(0);
    }

    [Fact]
    public void GivenAllSkippedResults_WhenClassifyingExitCode_ThenReturnsZero()
    {
        // Arrange
        var results = new List<ImplReportResult> { MakeResult("skipped") };

        // Act
        var code = RunCommand.ClassifyExitCode(results);

        // Assert
        code.ShouldBe(0);
    }

    [Fact]
    public void GivenFailResult_WhenClassifyingExitCode_ThenReturnsOne()
    {
        // Arrange
        var results = new List<ImplReportResult>
        {
            MakeResult("pass"),
            MakeResult("fail")
        };

        // Act
        var code = RunCommand.ClassifyExitCode(results);

        // Assert
        code.ShouldBe(1);
    }

    [Fact]
    public void GivenErrorResult_WhenClassifyingExitCode_ThenReturnsOne()
    {
        // Arrange
        var results = new List<ImplReportResult>
        {
            MakeResult("pass"),
            MakeResult("error")
        };

        // Act
        var code = RunCommand.ClassifyExitCode(results);

        // Assert
        code.ShouldBe(1);
    }

    [Fact]
    public void GivenMixOfPassAndSkipped_WhenClassifyingExitCode_ThenReturnsZero()
    {
        // Arrange
        var results = new List<ImplReportResult>
        {
            MakeResult("pass"),
            MakeResult("skipped"),
            MakeResult("pass")
        };

        // Act
        var code = RunCommand.ClassifyExitCode(results);

        // Assert
        code.ShouldBe(0);
    }

    [Fact]
    public void GivenEmptyResultsList_WhenClassifyingExitCode_ThenReturnsZero()
    {
        // Act
        var code = RunCommand.ClassifyExitCode([]);

        // Assert
        code.ShouldBe(0);
    }
}
