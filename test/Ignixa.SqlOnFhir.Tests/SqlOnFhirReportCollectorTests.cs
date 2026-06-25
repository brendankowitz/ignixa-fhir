/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for SqlOnFhirReportCollector: round-trip serialization, write-path resilience,
 * and output-path resolution.
 */

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Round-trip and resilience tests for <see cref="SqlOnFhirReportCollector"/>. These drive the
/// internal <c>WriteReport</c> seam with an explicit path and never touch the process-global
/// <c>SOF_TEST_REPORT_PATH</c> environment variable, so they are safe to run in parallel with the
/// live conformance collection.
/// </summary>
public class SqlOnFhirReportCollectorTests
{
    [Fact]
    public void GivenRecordedOutcomes_WhenWriteReport_ThenJsonPreservesOrderAndReasonOmission()
    {
        // Arrange
        using var collector = new SqlOnFhirReportCollector();
        collector.Record("a.json", "first passes", SqlOnFhirTestCaseOutcome.Pass());
        collector.Record("a.json", "second fails", SqlOnFhirTestCaseOutcome.Fail("boom"));
        collector.Record("b.json", "only passes", SqlOnFhirTestCaseOutcome.Pass());

        var tempFile = Path.Combine(Path.GetTempPath(), $"sof_report_{Guid.NewGuid():N}.json");

        try
        {
            // Act
            collector.WriteReport(tempFile);

            // Assert
            var json = File.ReadAllText(tempFile);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.True(root.TryGetProperty("a.json", out var aFile));
            Assert.True(root.TryGetProperty("b.json", out _));

            var aTests = aFile.GetProperty("tests");
            Assert.Equal(2, aTests.GetArrayLength());

            var firstResult = aTests[0].GetProperty("result");
            Assert.True(firstResult.GetProperty("passed").GetBoolean());
            Assert.False(firstResult.TryGetProperty("reason", out _));

            var secondResult = aTests[1].GetProperty("result");
            Assert.False(secondResult.GetProperty("passed").GetBoolean());
            Assert.True(secondResult.TryGetProperty("reason", out var reason));
            Assert.Equal("boom", reason.GetString());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void GivenUnwritablePath_WhenWriteReport_ThenLogsAndDoesNotThrow()
    {
        // Arrange
        using var collector = new SqlOnFhirReportCollector();
        collector.Record("a.json", "passes", SqlOnFhirTestCaseOutcome.Pass());

        var directoryPath = Path.GetTempPath();

        // Act
        var exception = Record.Exception(() => collector.WriteReport(directoryPath));

        // Assert
        Assert.Null(exception);
    }
}

/// <summary>
/// Path-resolution test that mutates the process-global <c>SOF_TEST_REPORT_PATH</c> environment
/// variable. It joins the <c>SqlOnFhirReport</c> collection so it is serialized with the live
/// conformance collection and cannot run concurrently with that fixture's disposal.
/// </summary>
[Collection("SqlOnFhirReport")]
public class SqlOnFhirReportPathResolutionTests
{
    private const string EnvVarName = "SOF_TEST_REPORT_PATH";

    [Fact]
    public void GivenEnvVar_WhenResolveOutputPath_ThenHonorsOverrideClearAndWhitespace()
    {
        var original = Environment.GetEnvironmentVariable(EnvVarName);

        try
        {
            var explicitPath = Path.Combine(Path.GetTempPath(), "custom_report.json");
            Environment.SetEnvironmentVariable(EnvVarName, explicitPath);
            Assert.Equal(explicitPath, SqlOnFhirReportCollector.ResolveOutputPath());

            Environment.SetEnvironmentVariable(EnvVarName, null);
            Assert.EndsWith("test_report.json", SqlOnFhirReportCollector.ResolveOutputPath(), StringComparison.Ordinal);

            Environment.SetEnvironmentVariable(EnvVarName, "   ");
            Assert.EndsWith("test_report.json", SqlOnFhirReportCollector.ResolveOutputPath(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }
}
