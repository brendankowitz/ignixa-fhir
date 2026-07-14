using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Reporting;

public static class TestReportResourceGenerator
{
    public static JsonObject Generate(TestScriptReport report, TestReportContext? context = null)
    {
        var testReport = new JsonObject
        {
            ["resourceType"] = "TestReport",
            ["name"] = report.TestScriptName,
            ["status"] = "completed",
            ["testScript"] = GenerateTestScriptReference(report, context),
            ["result"] = MapReportResult(report.OverallOutcome),
            ["score"] = ComputeScore(report.TestResults),
            ["issued"] = report.EndTime.ToString("o"),
            ["participant"] = GenerateParticipants(context)
        };

        if (context?.Tester is { } tester)
            testReport["tester"] = tester;

        if (report.SetupResult is not null)
            testReport["setup"] = GenerateSetup(report.SetupResult);

        if (report.TestResults.Count > 0)
            testReport["test"] = GenerateTests(report.TestResults);

        if (report.TeardownResult is not null)
            testReport["teardown"] = GenerateTeardown(report.TeardownResult);

        return testReport;
    }

    // TestReport.testScript is 1..1, so it is always emitted. A display-only Reference satisfies
    // that without asserting a resolvable location for a script that only exists as a file on the
    // runner's disk — a relative path in Reference.reference would be read as [type]/[id].
    private static JsonObject GenerateTestScriptReference(TestScriptReport report, TestReportContext? context) =>
        new() { ["display"] = context?.TestScriptDisplay ?? report.TestScriptName };

    // TestReportContext normalizes blank values to null, so a plain null check is enough here and
    // an empty TestScriptDisplay falls back to the script name rather than emitting "display": "".

    // participant.uri is 1..1, so the server entry only appears once a URI is known. The
    // test-engine entry is this library, which is true regardless of what the caller supplies.
    private static JsonArray GenerateParticipants(TestReportContext? context)
    {
        var participants = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "test-engine",
                ["uri"] = "urn:ignixa:testscript-engine",
                ["display"] = "Ignixa.TestScript"
            }
        };

        if (context?.ServerUri is { } serverUri)
        {
            participants.Insert(0, new JsonObject
            {
                ["type"] = "server",
                ["uri"] = serverUri
            });
        }

        return participants;
    }

    // TestReport.score is the percentage of tests that passed. Warning is a passing outcome for
    // the report as a whole (see MapReportResult), so it counts toward the numerator here too.
    private static double ComputeScore(IReadOnlyList<TestCaseResult> tests)
    {
        if (tests.Count == 0)
            return 0;

        var passed = tests.Count(t => t.Outcome is TestScriptOutcome.Pass or TestScriptOutcome.Warning);
        return Math.Round(100d * passed / tests.Count);
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

    private const string AssertionAnyOfGroupUrl = "http://ignixa.io/testscript/assertionAnyOfGroup";
    private const string AssertionGroupMemberUrl = "http://ignixa.io/testscript/assertionGroupMember";

    private static JsonObject GenerateAction(ActionResult action)
    {
        var obj = new JsonObject
        {
            ["result"] = MapActionResult(action.Outcome)
        };
        if (action.Label is not null) obj["id"] = action.Label;
        if (action.Message is not null) obj["message"] = action.Message;
        if (action.Description is not null) obj["detail"] = action.Description;

        var extensions = new JsonArray();
        if (action.GroupId is not null)
            extensions.Add(new JsonObject { ["url"] = AssertionAnyOfGroupUrl, ["valueString"] = action.GroupId });
        if (action.Members is { Count: > 0 })
            foreach (var member in GenerateGroupMemberExtensions(action.Members))
                extensions.Add(member);

        if (extensions.Count > 0)
            obj["extension"] = extensions;

        return obj;
    }

    private static List<JsonObject> GenerateGroupMemberExtensions(IReadOnlyList<AssertionGroupMemberResult> members)
    {
        var result = new List<JsonObject>();
        foreach (var member in members)
        {
            var children = new JsonArray
            {
                new JsonObject { ["url"] = "applicable", ["valueBoolean"] = member.Applicable },
                new JsonObject { ["url"] = "passed", ["valueBoolean"] = member.Passed }
            };
            if (member.Description is not null)
                children.Add(new JsonObject { ["url"] = "description", ["valueString"] = member.Description });
            if (member.Message is not null)
                children.Add(new JsonObject { ["url"] = "message", ["valueString"] = member.Message });

            result.Add(new JsonObject
            {
                ["url"] = AssertionGroupMemberUrl,
                ["extension"] = children
            });
        }
        return result;
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
