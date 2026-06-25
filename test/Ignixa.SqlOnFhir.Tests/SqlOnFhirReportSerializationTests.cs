/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests verifying the emitted report JSON matches the sql-on-fhir.js report format.
 */

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Verifies the serialized report has the sql-on-fhir.js shape:
/// <c>tests</c>/<c>name</c>/<c>result</c>/<c>passed</c>, with <c>reason</c> omitted when null.
/// Assertions navigate the parsed JSON structure rather than matching raw substrings so they
/// are not coupled to serializer formatting.
/// </summary>
public class SqlOnFhirReportSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [Fact]
    public void GivenPassingAndFailingEntries_WhenSerialized_ThenMatchesReportFormat()
    {
        // Arrange
        var fileReport = new SqlOnFhirFileReport();
        fileReport.Tests.Add(new SqlOnFhirTestEntry("filtering with 'and'", new SqlOnFhirTestResult(true, null)));
        fileReport.Tests.Add(new SqlOnFhirTestEntry("filtering with 'or'", new SqlOnFhirTestResult(false, "skipped")));

        var report = new Dictionary<string, SqlOnFhirFileReport> { ["logic.json"] = fileReport };

        // Act
        var json = JsonSerializer.Serialize(report, SerializerOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        var tests = document.RootElement.GetProperty("logic.json").GetProperty("tests");

        var first = tests[0];
        Assert.Equal("filtering with 'and'", first.GetProperty("name").GetString());
        var firstResult = first.GetProperty("result");
        Assert.True(firstResult.GetProperty("passed").GetBoolean());
        Assert.False(firstResult.TryGetProperty("reason", out _));

        var second = tests[1];
        Assert.Equal("filtering with 'or'", second.GetProperty("name").GetString());
        var secondResult = second.GetProperty("result");
        Assert.False(secondResult.GetProperty("passed").GetBoolean());
        Assert.True(secondResult.TryGetProperty("reason", out var reason));
        Assert.Equal("skipped", reason.GetString());
    }

    [Fact]
    public void GivenNullReason_WhenSerialized_ThenReasonOmitted()
    {
        // Arrange
        var result = new SqlOnFhirTestResult(true, null);

        // Act
        var json = JsonSerializer.Serialize(result, SerializerOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("reason", out _));
    }

    [Fact]
    public void GivenPresentReason_WhenSerialized_ThenReasonIncluded()
    {
        // Arrange
        var result = new SqlOnFhirTestResult(false, "row mismatch");

        // Act
        var json = JsonSerializer.Serialize(result, SerializerOptions);

        // Assert
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("passed").GetBoolean());
        Assert.True(document.RootElement.TryGetProperty("reason", out var reason));
        Assert.Equal("row mismatch", reason.GetString());
    }
}
