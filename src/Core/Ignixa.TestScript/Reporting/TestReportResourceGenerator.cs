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
            ["result"] = MapOutcome(report.OverallOutcome),
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

    private static JsonObject GenerateSetup(TestPhaseResult setup)
    {
        var actions = new JsonArray();
        foreach (var action in setup.Actions)
            actions.Add(GenerateAction(action));
        return new JsonObject { ["action"] = actions };
    }

    private static JsonArray GenerateTests(IReadOnlyList<TestCaseResult> tests)
    {
        var result = new JsonArray();
        foreach (var test in tests)
        {
            var actions = new JsonArray();
            foreach (var action in test.Actions)
                actions.Add(GenerateAction(action));

            result.Add(new JsonObject
            {
                ["name"] = test.Name,
                ["description"] = test.Description,
                ["action"] = actions
            });
        }
        return result;
    }

    private static JsonObject GenerateTeardown(TestPhaseResult teardown)
    {
        var actions = new JsonArray();
        foreach (var action in teardown.Actions)
            actions.Add(GenerateAction(action));
        return new JsonObject { ["action"] = actions };
    }

    private static JsonObject GenerateAction(ActionResult action)
    {
        var obj = new JsonObject
        {
            ["result"] = MapOutcome(action.Outcome)
        };
        if (action.Label is not null) obj["id"] = action.Label;
        if (action.Message is not null) obj["message"] = action.Message;
        if (action.Description is not null) obj["detail"] = action.Description;
        return obj;
    }

    private static string MapOutcome(TestScriptOutcome outcome) => outcome switch
    {
        TestScriptOutcome.Pass => "pass",
        TestScriptOutcome.Fail => "fail",
        TestScriptOutcome.Error => "error",
        TestScriptOutcome.Skip => "skip",
        _ => "error"
    };
}
