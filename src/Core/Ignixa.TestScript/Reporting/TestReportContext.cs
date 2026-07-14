namespace Ignixa.TestScript.Reporting;

/// <summary>
/// Run-scoped facts the engine cannot infer from a <see cref="TestScriptReport"/> alone — which
/// server was exercised, who ran the script, and what to call the script itself — supplied by the
/// caller so <see cref="TestReportResourceGenerator"/> can populate the matching TestReport
/// elements. Every member is optional; omitting one drops its element rather than inventing a value.
/// </summary>
/// <remarks>
/// Blank and whitespace-only values normalize to <c>null</c> on construction, so "absent" has
/// exactly one representation. FHIR forbids empty strings, so a caller passing <c>""</c> means
/// "I have no value" — treating that as present would emit an invalid resource.
/// </remarks>
public sealed record TestReportContext
{
    private readonly string? _tester;
    private readonly Uri? _serverUri;
    private readonly string? _serverDisplay;
    private readonly string? _testScriptDisplay;

    /// <summary><c>TestReport.tester</c> — the organisation or tool that executed the script.</summary>
    public string? Tester
    {
        get => _tester;
        init => _tester = Normalize(value);
    }

    /// <summary>Base URL of the server under test, emitted as the <c>server</c> participant's <c>uri</c>.</summary>
    public Uri? ServerUri
    {
        get => _serverUri;
        init => _serverUri = value is not null && !value.IsAbsoluteUri
            ? throw new ArgumentException("ServerUri must be an absolute URI.", nameof(value))
            : value;
    }

    /// <summary>
    /// Human-readable name of the server under test, emitted as the <c>server</c> participant's
    /// <c>display</c>.
    /// </summary>
    public string? ServerDisplay
    {
        get => _serverDisplay;
        init => _serverDisplay = Normalize(value);
    }

    /// <summary>
    /// <c>TestReport.testScript.display</c> — how to name the source script, e.g. a suite-relative
    /// path like <c>Search/intervals.json</c>. Defaults to the report's TestScript name.
    /// </summary>
    public string? TestScriptDisplay
    {
        get => _testScriptDisplay;
        init => _testScriptDisplay = Normalize(value);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
