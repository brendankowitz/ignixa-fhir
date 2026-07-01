using Ignixa.Serialization;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Reporting;

namespace Ignixa.Api.E2ETests.Conformance;

internal static class ConformanceReportMapper
{
    private const int MaxBodyLength = 32_768;
    private const string Redacted = "[redacted]";

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
        "Set-Cookie",
        "X-Api-Key"
    };

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
            BuildError(testCase.Actions))
        {
            Steps = MapSteps(report.SetupResult?.Actions ?? [], "setup")
                .Concat(MapSteps(testCase.Actions, "test"))
                .Concat(MapSteps(report.TeardownResult?.Actions ?? [], "teardown"))
                .ToList()
        };
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
                {
                    Steps = MapSteps(report.SetupResult?.Actions ?? [], "setup")
                }
            ];
        }

        return report.TestResults
            .Select(testCase => new ConformanceResult(
                $"{report.TestScriptName} > {testCase.Name}",
                relativeFile,
                setupStatus,
                0,
                setupError)
            {
                Steps = MapSteps(report.SetupResult?.Actions ?? [], "setup")
            })
            .ToList();
    }

    private static IReadOnlyList<ConformanceStep> MapSteps(IReadOnlyList<ActionResult> actions, string phase) =>
        actions.Select(action => MapStep(action, phase)).ToList();

    private static ConformanceStep MapStep(ActionResult action, string phase)
    {
        var exchange = action.Exchange;
        return new ConformanceStep(
            phase,
            action.Kind == TestActionKind.Operation ? "operation" : "assertion",
            action.Label,
            action.Description,
            MapStatus(action.Outcome),
            (long)Math.Round(action.Duration.TotalMilliseconds),
            action.Message,
            exchange?.Request is null ? null : MapRequest(exchange.Request),
            exchange?.Response is null ? null : MapResponse(exchange.Response));
    }

    private static ConformanceHttpRequest MapRequest(TestRequest request) =>
        new(
            request.Method.Method,
            request.Url,
            RedactHeaders(request.Headers),
            TruncateBody(request.FormBody ?? request.Body?.SerializeToString()));

    private static ConformanceHttpResponse MapResponse(TestResponse response) =>
        new(
            response.StatusCode,
            RedactHeaders(response.Headers),
            TruncateBody(response.RawBody ?? response.Body?.SerializeToString()),
            response.BodyParseError);

    private static IReadOnlyDictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers) =>
        headers
            .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                header => header.Key,
                header => SensitiveHeaders.Contains(header.Key) ? Redacted : header.Value,
                StringComparer.OrdinalIgnoreCase);

    private static string? TruncateBody(string? body)
    {
        if (string.IsNullOrEmpty(body) || body.Length <= MaxBodyLength)
            return body;

        return $"{body[..MaxBodyLength]}\n... [truncated]";
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
