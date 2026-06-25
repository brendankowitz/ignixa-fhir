/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for SqlOnFhirTestCaseEvaluator: a known-pass and a known-fail synthetic case.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Unit tests for <see cref="SqlOnFhirTestCaseEvaluator"/> using minimal in-memory cases.
/// </summary>
public class SqlOnFhirTestCaseEvaluatorTests
{
    private const string PatientResourceJson = """
        { "resourceType": "Patient", "id": "pt1", "active": true }
        """;

    private const string ViewJson = """
        {
          "resource": "Patient",
          "select": [ { "column": [ { "name": "id", "path": "id" } ] } ]
        }
        """;

    [Fact]
    public void GivenMatchingExpectedRows_WhenEvaluated_ThenOutcomePassed()
    {
        // Arrange
        var testFile = BuildTestFile();
        var testCase = BuildTestCase(
            expectedRows: [new Dictionary<string, object?> { ["id"] = "pt1" }]);

        // Act
        var outcome = SqlOnFhirTestCaseEvaluator.Run(testFile, testCase);

        // Assert
        Assert.True(outcome.Passed, outcome.Reason);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void GivenMismatchedExpectedRows_WhenEvaluated_ThenOutcomeFailedWithReason()
    {
        // Arrange
        var testFile = BuildTestFile();
        var testCase = BuildTestCase(
            expectedRows: [new Dictionary<string, object?> { ["id"] = "does-not-match" }]);

        // Act
        var outcome = SqlOnFhirTestCaseEvaluator.Run(testFile, testCase);

        // Assert
        Assert.False(outcome.Passed);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
    }

    private static SqlOnFhirTestFile BuildTestFile()
    {
        using var document = JsonDocument.Parse(PatientResourceJson);
        var resources = new List<JsonElement> { document.RootElement.Clone() };

        return new SqlOnFhirTestFile
        {
            Title = "synthetic",
            Description = "synthetic test file",
            FhirVersions = ["4.0.1"],
            Resources = resources,
            Tests = []
        };
    }

    private static SqlOnFhirTestCase BuildTestCase(List<Dictionary<string, object?>> expectedRows)
    {
        var viewNode = JsonNodeSourceNode.Create(JsonNode.Parse(ViewJson)!, "ViewDefinition");

        return new SqlOnFhirTestCase
        {
            Title = "synthetic case",
            Tags = [],
            ViewNode = viewNode,
            ExpectedRows = expectedRows
        };
    }
}
