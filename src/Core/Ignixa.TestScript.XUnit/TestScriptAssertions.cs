using Ignixa.TestScript.Reporting;
using Shouldly;

namespace Ignixa.TestScript.XUnit;

public static class TestScriptAssertions
{
    public static void ShouldPass(this TestScriptReport report)
    {
        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass,
            $"TestScript '{report.TestScriptName}' expected to pass but outcome was {report.OverallOutcome}");
    }

    public static void ShouldFail(this TestScriptReport report)
    {
        report.OverallOutcome.ShouldBe(TestScriptOutcome.Fail,
            $"TestScript '{report.TestScriptName}' expected to fail but outcome was {report.OverallOutcome}");
    }

    public static void ShouldHaveTestCount(this TestScriptReport report, int expectedCount)
    {
        report.TestResults.Count.ShouldBe(expectedCount,
            $"TestScript '{report.TestScriptName}' expected {expectedCount} test(s) but had {report.TestResults.Count}");
    }

    public static void ShouldHavePassingSetup(this TestScriptReport report)
    {
        report.SetupResult.ShouldNotBeNull(
            $"TestScript '{report.TestScriptName}' has no setup result");
        report.SetupResult.Outcome.ShouldBe(TestScriptOutcome.Pass,
            $"TestScript '{report.TestScriptName}' setup expected to pass but was {report.SetupResult.Outcome}");
    }

    public static void ShouldHavePassingTeardown(this TestScriptReport report)
    {
        report.TeardownResult.ShouldNotBeNull(
            $"TestScript '{report.TestScriptName}' has no teardown result");
        report.TeardownResult.Outcome.ShouldBe(TestScriptOutcome.Pass,
            $"TestScript '{report.TestScriptName}' teardown expected to pass but was {report.TeardownResult.Outcome}");
    }
}
