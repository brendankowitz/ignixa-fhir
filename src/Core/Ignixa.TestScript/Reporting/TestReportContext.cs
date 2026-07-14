namespace Ignixa.TestScript.Reporting;

/// <summary>
/// Run-scoped facts the engine cannot infer from a <see cref="TestScriptReport"/> alone — which
/// server was exercised, who ran the script, and what to call the script itself — supplied by the
/// caller so <see cref="TestReportResourceGenerator"/> can populate the matching TestReport
/// elements. Every member is optional; omitting one drops its element rather than inventing a value.
/// </summary>
public sealed record TestReportContext
{
    /// <summary><c>TestReport.tester</c> — the organisation or tool that executed the script.</summary>
    public string? Tester { get; init; }

    /// <summary>Base URL of the server under test, emitted as the <c>server</c> participant.</summary>
    public string? ServerUri { get; init; }

    /// <summary>
    /// <c>TestReport.testScript.display</c> — how to name the source script, e.g. a suite-relative
    /// path like <c>Search/intervals.json</c>. Defaults to the report's TestScript name.
    /// </summary>
    public string? TestScriptDisplay { get; init; }
}
