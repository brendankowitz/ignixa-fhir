using Ignixa.TestScript.Reporting;

namespace Ignixa.Api.E2ETests.Conformance;

internal static class ConformanceReportMapper
{
    public static IReadOnlyList<ConformanceResult> Map(TestScriptReport report, string relativeFile)
    {
        var setupFailed = report.SetupResult?.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error;
        if (setupFailed)
            return MapSetupFailure(report, relativeFile);

        if (report.TestResults.Count == 0)
        {
            return
            [
                new ConformanceResult(
                    report.TestScriptName,
                    relativeFile,
                    "skipped",
                    0,
                    new ConformanceError("No tests", "Script contained no test cases"))
            ];
        }

        return report.TestResults
            .Select(testCase => MapTestCase(report, testCase, relativeFile))
            .ToList();
    }

    internal static string MapStatus(TestScriptOutcome outcome) => outcome switch
    {
        TestScriptOutcome.Pass or TestScriptOutcome.Warning => "pass",
        TestScriptOutcome.Fail => "fail",
        TestScriptOutcome.Error => "error",
        TestScriptOutcome.Skip => "skipped",
        _ => throw new InvalidOperationException($"Unhandled TestScriptOutcome value: {outcome}")
    };

    private static ConformanceResult MapTestCase(
        TestScriptReport report,
        TestCaseResult testCase,
        string relativeFile)
    {
        var durationMs = (long)Math.Round(testCase.Actions.Sum(action => action.Duration.TotalMilliseconds));
        return new ConformanceResult(
            $"{report.TestScriptName} > {testCase.Name}",
            relativeFile,
            MapStatus(testCase.Outcome),
            durationMs,
            BuildError(testCase.Actions));
    }

    private static IReadOnlyList<ConformanceResult> MapSetupFailure(TestScriptReport report, string relativeFile)
    {
        var setupError = BuildSetupError(report.SetupResult);
        var setupStatus = MapStatus(report.SetupResult?.Outcome ?? TestScriptOutcome.Fail);
        if (report.TestResults.Count == 0)
        {
            return
            [
                new ConformanceResult(report.TestScriptName, relativeFile, setupStatus, 0, setupError)
            ];
        }

        return report.TestResults
            .Select(testCase => new ConformanceResult(
                $"{report.TestScriptName} > {testCase.Name}",
                relativeFile,
                setupStatus,
                0,
                setupError))
            .ToList();
    }

    private static ConformanceError? BuildError(IReadOnlyList<ActionResult> actions)
    {
        var failing = actions.FirstOrDefault(action => action.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error);
        return failing is null
            ? null
            : new ConformanceError(
                failing.Description ?? failing.Label ?? "Assertion failed",
                failing.Message ?? "");
    }

    private static ConformanceError? BuildSetupError(TestPhaseResult? setup)
    {
        if (setup is null)
            return null;

        var failing = setup.Actions.FirstOrDefault(action => action.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error);
        return new ConformanceError(
            failing?.Description ?? failing?.Label ?? "Setup failed",
            failing?.Message ?? "(no error details captured)");
    }
}
