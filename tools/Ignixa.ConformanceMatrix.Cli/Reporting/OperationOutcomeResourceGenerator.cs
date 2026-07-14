using System.Text.Json.Nodes;

namespace Ignixa.ConformanceMatrix.Cli.Reporting;

/// <summary>
/// Builds FHIR <c>OperationOutcome</c> resources recording scripts the runner could not execute.
/// </summary>
/// <remarks>
/// A script that never ran has no TestReport to speak for it: <c>TestReport.testScript</c> references
/// the TestScript that was executed, and a file that failed to parse never became one. The status
/// codes that look close are not — <c>entered-in-error</c> means the report itself was created in
/// error (a retraction, not "the test errored") and <c>stopped</c> means a human halted the run.
/// OperationOutcome is what FHIR defines for "this could not be performed", and
/// <c>Bundle.type = collection</c> places no homogeneity constraint on entries, so these sit
/// alongside TestReports legally.
/// </remarks>
internal static class OperationOutcomeResourceGenerator
{
    /// <summary>"unable to parse the content completely, invalid syntax".</summary>
    public const string StructureIssueCode = "structure";

    /// <summary>"An unexpected internal error has occurred".</summary>
    public const string ExceptionIssueCode = "exception";

    // OperationOutcome.issue is 1..*, so exactly one issue is always emitted. Severity is fixed at
    // error because every caller here is reporting a script that did not run.
    public static JsonObject Generate(string issueCode, string diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostics);

        return new JsonObject
        {
            ["resourceType"] = "OperationOutcome",
            ["issue"] = new JsonArray
            {
                new JsonObject
                {
                    ["severity"] = "error",
                    ["code"] = issueCode,
                    ["diagnostics"] = diagnostics
                }
            }
        };
    }
}
