/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests driving SqlOnFhirTestCaseEvaluator.Run through its row/column comparison branches.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Exercises the comparison branches of <see cref="SqlOnFhirTestCaseEvaluator"/> (row-count
/// mismatch, column-order mismatch, and numeric cross-type equality) by driving full cases
/// through <c>Run</c>.
/// </summary>
public class SqlOnFhirTestCaseEvaluatorComparisonTests
{
    private const string PatientResourceJson = """
        { "resourceType": "Patient", "id": "pt1", "active": true, "multipleBirthInteger": 5 }
        """;

    private const string TwoColumnViewJson = """
        {
          "resource": "Patient",
          "select": [ { "column": [ { "name": "id", "path": "id" }, { "name": "active", "path": "active" } ] } ]
        }
        """;

    private const string NumericViewJson = """
        {
          "resource": "Patient",
          "select": [ { "column": [ { "name": "births", "path": "multipleBirthInteger" } ] } ]
        }
        """;

    [Fact]
    public void GivenTooManyExpectedRows_WhenEvaluated_ThenFailsWithRowCountReason()
    {
        // Arrange
        var testFile = BuildTestFile();
        var testCase = BuildTestCase(
            TwoColumnViewJson,
            expectedRows:
            [
                new Dictionary<string, object?> { ["id"] = "pt1", ["active"] = true },
                new Dictionary<string, object?> { ["id"] = "pt2", ["active"] = false }
            ]);

        // Act
        var outcome = SqlOnFhirTestCaseEvaluator.Run(testFile, testCase);

        // Assert
        Assert.False(outcome.Passed);
        Assert.Contains("Expected 2 rows but got 1", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenColumnsInWrongOrder_WhenEvaluated_ThenFailsWithColumnOrderReason()
    {
        // Arrange
        var testFile = BuildTestFile();
        var testCase = BuildTestCase(
            TwoColumnViewJson,
            expectedRows: [new Dictionary<string, object?> { ["id"] = "pt1", ["active"] = true }],
            expectedColumns: ["active", "id"]);

        // Act
        var outcome = SqlOnFhirTestCaseEvaluator.Run(testFile, testCase);

        // Assert
        Assert.False(outcome.Passed);
        Assert.Contains("Column order mismatch", outcome.Reason, StringComparison.Ordinal);
        Assert.Contains("active", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDecimalExpectedAndIntegerActual_WhenEvaluated_ThenPassesViaNumericNormalization()
    {
        // Arrange
        var testFile = BuildTestFile();
        var testCase = BuildTestCase(
            NumericViewJson,
            expectedRows: [new Dictionary<string, object?> { ["births"] = 5.0m }]);

        // Act
        var outcome = SqlOnFhirTestCaseEvaluator.Run(testFile, testCase);

        // Assert
        Assert.True(outcome.Passed, outcome.Reason);
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

    private static SqlOnFhirTestCase BuildTestCase(
        string viewJson,
        List<Dictionary<string, object?>> expectedRows,
        List<string>? expectedColumns = null)
    {
        var viewNode = JsonNodeSourceNode.Create(JsonNode.Parse(viewJson)!, "ViewDefinition");

        return new SqlOnFhirTestCase
        {
            Title = "synthetic case",
            Tags = [],
            ViewNode = viewNode,
            ExpectedRows = expectedRows,
            ExpectedColumns = expectedColumns ?? []
        };
    }
}
