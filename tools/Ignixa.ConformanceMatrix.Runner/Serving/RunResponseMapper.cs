using System.Text;
using Ignixa.TestScript.Reporting;

namespace Ignixa.ConformanceMatrix.Runner.Serving;

/// <summary>
/// Maps an evaluator <see cref="TestScriptReport"/> to the runner's wire-level <see cref="RunResponse"/>.
/// Operation ordering mirrors execution order: setup, then each test case in order, then teardown.
/// </summary>
internal static class RunResponseMapper
{
    public static RunResponse Map(TestScriptReport report, string testScriptId)
    {
        ArgumentNullException.ThrowIfNull(report);

        var actions = EnumerateActions(report).ToList();
        var operations = actions
            .Where(a => a.Kind == TestActionKind.Operation)
            .Select(MapOperation)
            .ToList();

        var passed = IsPassing(report.OverallOutcome);
        var failedAssertionCount = actions.Count(a =>
            a.Kind == TestActionKind.Assertion && a.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error);
        var durationMs = (long)(report.EndTime - report.StartTime).TotalMilliseconds;

        return new RunResponse(
            passed,
            testScriptId,
            durationMs,
            failedAssertionCount,
            BuildSummary(report, passed, actions),
            operations);
    }

    private static bool IsPassing(TestScriptOutcome outcome) => outcome is TestScriptOutcome.Pass or TestScriptOutcome.Warning;

    private static IEnumerable<ActionResult> EnumerateActions(TestScriptReport report)
    {
        if (report.SetupResult is { } setup)
            foreach (var action in setup.Actions)
                yield return action;

        foreach (var test in report.TestResults)
            foreach (var action in test.Actions)
                yield return action;

        if (report.TeardownResult is { } teardown)
            foreach (var action in teardown.Actions)
                yield return action;
    }

    private static OperationResult MapOperation(ActionResult action)
    {
        var request = action.Exchange?.Request;
        var response = action.Exchange?.Response;
        var responseBytes = response?.RawBody is { } rawBody ? Encoding.UTF8.GetByteCount(rawBody) : 0;

        return new OperationResult(
            action.Label ?? action.Description ?? "(operation)",
            request?.Method.Method ?? string.Empty,
            request?.Url ?? string.Empty,
            response?.StatusCode ?? 0,
            (long)action.Duration.TotalMilliseconds,
            responseBytes,
            IsPassing(action.Outcome));
    }

    /// <summary>
    /// "Passed" when the run passed; otherwise the overall outcome plus the first failing action
    /// (across setup, tests, and teardown, in execution order) so a caller doesn't need to walk the
    /// full <see cref="TestScriptReport"/> just to see why a run failed.
    /// </summary>
    private static string BuildSummary(TestScriptReport report, bool passed, IReadOnlyList<ActionResult> actions)
    {
        if (passed)
            return "Passed";

        var failing = actions.FirstOrDefault(a => a.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error);
        var label = failing?.Label ?? failing?.Description ?? "(unlabeled action)";
        var message = failing?.Message ?? "no diagnostic message recorded";
        return $"{report.OverallOutcome}: {label} - {message}";
    }
}
