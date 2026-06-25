/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Guards that the emitted test_report.json covers the official suite completely and in order.
 * The registry report viewer matches each implementation's results positionally against the
 * canonical test list, so a missing file, a dropped test, or a reordered test would silently
 * misalign the conformance matrix. This re-parses the suite files independently and compares
 * against what the runner discovers (the same sequence the report collector records).
 */

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Verifies the conformance report is complete: every official test appears exactly once,
/// keyed by its file (with the <c>.json</c> extension) and in the suite's order.
/// </summary>
public class SqlOnFhirReportCoverageTests
{
    private static readonly string SuiteDirectory = Path.Combine(
        Path.GetDirectoryName(typeof(OfficialSqlOnFhirTestRunner).Assembly.Location) ?? "",
        "sql-on-fhir-tests", "tests");

    [Fact]
    public void GivenOfficialSuite_WhenDiscovered_ThenEveryTestIsCoveredInOrder()
    {
        // Arrange: read the expected (file -> ordered titles) straight from the suite JSON,
        // independently of the runner's own parser.
        Assert.True(Directory.Exists(SuiteDirectory), $"Suite directory missing (submodule not initialized?): {SuiteDirectory}");

        var suiteFiles = Directory.GetFiles(SuiteDirectory, "*.json");
        Assert.NotEmpty(suiteFiles);

        var expected = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in suiteFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var titles = new List<string>();
            if (document.RootElement.TryGetProperty("tests", out var tests))
            {
                titles.AddRange(tests.EnumerateArray().Select(t => t.GetProperty("title").GetString()!));
            }

            expected[Path.GetFileName(file)] = titles;
        }

        // Act: group the runner's discovered cases the way the collector keys the report.
        var discovered = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var testCase in OfficialSqlOnFhirTestRunner.GetOfficialTestCases())
        {
            var fileName = (string)testCase[0];
            if (testCase[2] is not SqlOnFhirTestCase sqlTestCase)
            {
                Assert.Fail($"Test file failed to parse and would be dropped from the report: {fileName}");
                continue;
            }

            var key = $"{fileName}.json";
            if (!discovered.TryGetValue(key, out var titles))
            {
                discovered[key] = titles = [];
            }

            titles.Add(sqlTestCase.Title);
        }

        // Assert: ordered parity per file, no missing/extra files, and equal totals.
        foreach (var (file, titles) in expected)
        {
            if (titles.Count == 0)
            {
                Assert.False(discovered.ContainsKey(file), $"File '{file}' has no tests but appears in the report");
                continue;
            }

            Assert.True(discovered.TryGetValue(file, out var discoveredTitles), $"Report would be missing file '{file}'");
            Assert.Equal(titles, discoveredTitles);
        }

        foreach (var file in discovered.Keys)
        {
            Assert.True(expected.ContainsKey(file), $"Report contains a file not in the suite: '{file}'");
        }

        Assert.Equal(expected.Values.Sum(v => v.Count), discovered.Values.Sum(v => v.Count));
    }
}
