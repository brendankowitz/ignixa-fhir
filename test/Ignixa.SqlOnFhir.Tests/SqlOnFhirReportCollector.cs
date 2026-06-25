/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * xunit collection fixture that accumulates SQL on FHIR conformance test outcomes
 * and writes them as a sql-on-fhir.js-format test_report.json when the collection completes.
 */

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Collection fixture that accumulates per-case conformance outcomes during a test run and
/// writes a sql-on-fhir.js-format <c>test_report.json</c> on dispose (after the last case).
/// </summary>
public sealed class SqlOnFhirReportCollector : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, SqlOnFhirFileReport> _reports = [];
    private bool _written;

    /// <summary>
    /// Records the outcome of a single test case under its source file (keyed by file name
    /// including the <c>.json</c> extension), appending entries in arrival order.
    /// </summary>
    public void Record(string fileName, string testName, SqlOnFhirTestCaseOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(testName);
        ArgumentNullException.ThrowIfNull(outcome);

        var entry = new SqlOnFhirTestEntry(testName, new SqlOnFhirTestResult(outcome.Passed, outcome.Reason));

        lock (_gate)
        {
            if (!_reports.TryGetValue(fileName, out var report))
            {
                report = new SqlOnFhirFileReport();
                _reports[fileName] = report;
            }

            report.Tests.Add(entry);
        }
    }

    /// <summary>
    /// Resolves the output path: <c>SOF_TEST_REPORT_PATH</c> if set, otherwise
    /// <c>test_report.json</c> next to the test assembly.
    /// </summary>
    public static string ResolveOutputPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("SOF_TEST_REPORT_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var assemblyDir = Path.GetDirectoryName(typeof(SqlOnFhirReportCollector).Assembly.Location) ?? ".";
        return Path.Combine(assemblyDir, "test_report.json");
    }

    public void Dispose() => WriteReport(ResolveOutputPath());

    /// <summary>
    /// Serializes the accumulated reports under the lock (so a concurrent <see cref="Record"/> cannot
    /// mutate a nested list mid-serialization) and writes them to <paramref name="outputPath"/>.
    /// A failure to write is logged and swallowed so a report-write problem never throws out of
    /// fixture disposal. Exposed (internal) so tests can drive the write path with an explicit path
    /// without touching the <c>SOF_TEST_REPORT_PATH</c> environment variable or <see cref="Dispose"/>.
    /// Once the report has been written, a subsequent <see cref="Dispose"/> is a no-op so an explicit
    /// write is not clobbered.
    /// </summary>
    internal void WriteReport(string outputPath)
    {
        string json;
        lock (_gate)
        {
            if (_written)
            {
                return;
            }

            _written = true;
            json = JsonSerializer.Serialize(_reports, SerializerOptions);
        }

        try
        {
            File.WriteAllText(outputPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"::error::Failed to write SQL-on-FHIR test report to '{outputPath}': {ex}");
        }
    }
}
