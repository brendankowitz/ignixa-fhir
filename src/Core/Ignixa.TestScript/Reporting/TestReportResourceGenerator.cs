using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Reporting;

public static class TestReportResourceGenerator
{
    public static JsonObject Generate(TestScriptReport report)
    {
        var testReport = new JsonObject
        {
            ["resourceType"] = "TestReport",
            ["name"] = report.TestScriptName,
            ["status"] = "completed",
            ["result"] = MapReportResult(report.OverallOutcome),
            ["issued"] = report.EndTime.ToString("o")
        };

        if (report.SetupResult is not null)
            testReport["setup"] = GenerateSetup(report.SetupResult);

        if (report.TestResults.Count > 0)
            testReport["test"] = GenerateTests(report.TestResults);

        if (report.TeardownResult is not null)
            testReport["teardown"] = GenerateTeardown(report.TeardownResult);

        return testReport;
    }

    private static JsonObject GenerateSetup(TestPhaseResult setup) =>
        new() { ["action"] = GenerateActionArray(setup.Actions) };

    private static JsonArray GenerateTests(IReadOnlyList<TestCaseResult> tests)
    {
        var result = new JsonArray();
        foreach (var test in tests)
        {
            result.Add(new JsonObject
            {
                ["name"] = test.Name,
                ["description"] = test.Description,
                ["action"] = GenerateActionArray(test.Actions)
            });
        }
        return result;
    }

    private static JsonObject GenerateTeardown(TestPhaseResult teardown) =>
        new() { ["action"] = GenerateActionArray(teardown.Actions) };

    private static JsonArray GenerateActionArray(IReadOnlyList<ActionResult> actions)
    {
        var array = new JsonArray();
        foreach (var action in actions)
            array.Add(GenerateAction(action));
        return array;
    }

    private static JsonObject GenerateAction(ActionResult action)
    {
        var obj = new JsonObject
        {
            ["result"] = MapActionResult(action.Outcome)
        };
        if (action.Label is not null) obj["id"] = action.Label;
        if (action.Message is not null) obj["message"] = action.Message;
        if (action.Description is not null) obj["detail"] = action.Description;
        return obj;
    }

    // Action-level results bind to the FHIR action-result valueset (pass | skip | fail | warning | error).
    private static string MapActionResult(TestScriptOutcome outcome) => outcome switch
    {
        TestScriptOutcome.Pass => "pass",
        TestScriptOutcome.Warning => "warning",
        TestScriptOutcome.Fail => "fail",
        TestScriptOutcome.Error => "error",
        TestScriptOutcome.Skip => "skip",
        _ => "error"
    };

    // TestReport.result binds to the narrower report-result-codes valueset (pass | fail | pending).
    // Warning is a passing run so it maps to pass; Error/Fail map to fail; Skip maps to pending
    // (the run never reached a definitive pass/fail).
    private static string MapReportResult(TestScriptOutcome outcome) => outcome switch
    {
        TestScriptOutcome.Pass => "pass",
        TestScriptOutcome.Warning => "pass",
        TestScriptOutcome.Fail => "fail",
        TestScriptOutcome.Error => "fail",
        TestScriptOutcome.Skip => "pending",
        _ => "fail"
    };
}
