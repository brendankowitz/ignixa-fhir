using Ignixa.TestScript.Reporting;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal static class ReportMapper
{
    public static IReadOnlyList<ImplReportResult> Map(TestScriptReport report, string relativeFilePath)
    {
        var setupFailed = report.SetupResult?.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error;

        if (setupFailed)
            return MapSetupFailure(report, relativeFilePath);

        if (report.TestResults.Count == 0)
            return [SyntheticSkip(report, relativeFilePath)];

        var results = new List<ImplReportResult>(report.TestResults.Count);
        foreach (var testCase in report.TestResults)
            results.Add(MapTestCase(report, testCase, relativeFilePath));

        return results;
    }

    private static ImplReportResult MapTestCase(TestScriptReport report, TestCaseResult testCase, string relativeFilePath)
    {
        var durationMs = (long)Math.Round(testCase.Actions.Sum(a => a.Duration.TotalMilliseconds));

        return new ImplReportResult
        {
            Id = $"{report.TestScriptName} > {testCase.Name}",
            File = relativeFilePath,
            Status = MapStatus(testCase.Outcome),
            DurationMs = durationMs,
            Error = BuildError(testCase.Actions)
        };
    }

    private static CellError? BuildError(IReadOnlyList<ActionResult> actions)
    {
        var failing = actions.FirstOrDefault(a => a.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error);
        if (failing is null)
            return null;

        return new CellError
        {
            Assertion = failing.Description ?? failing.Label ?? "Assertion failed",
            Received = failing.Message ?? ""
        };
    }

    private static IReadOnlyList<ImplReportResult> MapSetupFailure(TestScriptReport report, string relativeFilePath)
    {
        var setupError = BuildSetupError(report.SetupResult);

        if (report.TestResults.Count == 0)
        {
            return
            [
                new ImplReportResult
                {
                    Id = report.TestScriptName,
                    File = relativeFilePath,
                    Status = "fail",
                    DurationMs = 0,
                    Error = setupError
                }
            ];
        }

        var results = new List<ImplReportResult>(report.TestResults.Count);
        foreach (var testCase in report.TestResults)
        {
            results.Add(new ImplReportResult
            {
                Id = $"{report.TestScriptName} > {testCase.Name}",
                File = relativeFilePath,
                Status = "fail",
                DurationMs = 0,
                Error = setupError
            });
        }

        return results;
    }

    private static CellError? BuildSetupError(TestPhaseResult? setup)
    {
        if (setup is null)
            return null;

        var failing = setup.Actions.FirstOrDefault(a => a.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error);

        return new CellError
        {
            Assertion = failing?.Description ?? failing?.Label ?? "Setup failed",
            Received = failing?.Message ?? "(no error details captured)"
        };
    }

    private static ImplReportResult SyntheticSkip(TestScriptReport report, string relativeFilePath) =>
        new()
        {
            Id = report.TestScriptName,
            File = relativeFilePath,
            Status = "skipped",
            DurationMs = 0,
            Error = new CellError { Assertion = "No tests", Received = "Script contained no test cases" }
        };

    internal static string MapStatus(TestScriptOutcome outcome) => outcome switch
    {
        TestScriptOutcome.Pass or TestScriptOutcome.Warning => "pass",
        TestScriptOutcome.Fail or TestScriptOutcome.Error => "fail",
        TestScriptOutcome.Skip => "skipped",
        _ => throw new InvalidOperationException($"Unhandled TestScriptOutcome value: {outcome}")
    };
}
