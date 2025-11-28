/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Test file parser for SQL on FHIR v2 test suite.
 * Loads and parses official test files for evaluator validation.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.SqlOnFhir.Models;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Helper for parsing official SQL on FHIR v2 test files.
/// Loads test metadata and test cases from JSON.
/// </summary>
public static class TestFileParser
{
    /// <summary>
    /// Parses a JSON file containing ViewDefinition test cases.
    /// </summary>
    /// <param name="jsonFilePath">Path to the JSON test file</param>
    /// <returns>A test file containing metadata and test cases</returns>
    public static SqlOnFhirTestFile ParseTestFile(string jsonFilePath)
    {
        var json = File.ReadAllText(jsonFilePath);
        var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var fhirVersions = new List<string>();
        if (root.TryGetProperty("fhirVersion", out var fhirVersionsElement))
        {
            fhirVersions = fhirVersionsElement.EnumerateArray()
                .Select(v => v.GetString() ?? "")
                .ToList();
        }

        var resources = new List<JsonElement>();
        if (root.TryGetProperty("resources", out var resourcesElement))
        {
            resources = resourcesElement.EnumerateArray()
                .Select(r => r.Clone())
                .ToList();
        }

        var tests = new List<SqlOnFhirTestCase>();
        if (root.TryGetProperty("tests", out var testsElement))
        {
            foreach (var testElement in testsElement.EnumerateArray())
            {
                var testCase = ParseTestCase(testElement);
                tests.Add(testCase);
            }
        }

        var testFile = new SqlOnFhirTestFile
        {
            Title = root.GetProperty("title").GetString() ?? "",
            Description = root.GetProperty("description").GetString() ?? "",
            FhirVersions = fhirVersions,
            Resources = resources,
            Tests = tests
        };

        return testFile;
    }

    /// <summary>
    /// Parses a single test case from the JSON structure.
    /// </summary>
    private static SqlOnFhirTestCase ParseTestCase(JsonElement testElement)
    {
        var tags = new List<string>();
        if (testElement.TryGetProperty("tags", out var tagsElement))
        {
            tags = tagsElement.EnumerateArray()
                .Select(t => t.GetString() ?? "")
                .ToList();
        }

        var expectedRows = new List<Dictionary<string, object?>>();
        if (testElement.TryGetProperty("expect", out var expectElement))
        {
            expectedRows = expectElement.EnumerateArray()
                .Select(row => ParseRow(row))
                .ToList();
        }

        var testCase = new SqlOnFhirTestCase
        {
            Title = testElement.GetProperty("title").GetString() ?? "",
            Tags = tags,
            ExpectedRows = expectedRows
        };

        // Parse expectError flag
        if (testElement.TryGetProperty("expectError", out var expectErrorElement))
        {
            testCase.ExpectError = expectErrorElement.GetBoolean();
        }

        // Parse expected column order
        if (testElement.TryGetProperty("expectColumns", out var expectColumnsElement))
        {
            testCase.ExpectedColumns = expectColumnsElement.EnumerateArray()
                .Select(c => c.GetString() ?? "")
                .ToList();
        }

        // Parse the view definition - store as ISourceNavigator for direct use with ViewDefinitionExpressionParser
        if (testElement.TryGetProperty("view", out var viewElement))
        {
            // Convert JsonElement to JsonNode, then wrap as ISourceNavigator
            var viewJson = viewElement.GetRawText();
            var jsonNode = JsonNode.Parse(viewJson)!;
            testCase.ViewNode = JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");
        }

        return testCase;
    }

    /// <summary>
    /// Parses a single result row from JSON.
    /// </summary>
    private static Dictionary<string, object?> ParseRow(JsonElement rowElement)
    {
        var row = new Dictionary<string, object?>();

        foreach (var property in rowElement.EnumerateObject())
        {
            row[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText()
            };
        }

        return row;
    }
}

/// <summary>
/// Represents a SQL on FHIR test file with metadata and test cases.
/// </summary>
public class SqlOnFhirTestFile
{
    /// <summary>
    /// Title of the test file (e.g., "where", "foreach")
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Description of what the tests cover
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// FHIR versions this test supports
    /// </summary>
#pragma warning disable CA1002, CA2227 // Do not expose generic lists; Collections properties should be read-only
    public required List<string> FhirVersions { get; set; }
#pragma warning restore CA1002, CA2227

    /// <summary>
    /// Test resources in JSON/JsonElement format
    /// </summary>
#pragma warning disable CA1002, CA2227 // Do not expose generic lists; Collections properties should be read-only
    public required List<JsonElement> Resources { get; set; }
#pragma warning restore CA1002, CA2227

    /// <summary>
    /// Individual test cases
    /// </summary>
#pragma warning disable CA1002, CA2227 // Do not expose generic lists; Collections properties should be read-only
    public required List<SqlOnFhirTestCase> Tests { get; set; }
#pragma warning restore CA1002, CA2227
}

/// <summary>
/// Represents a single test case within a test file.
/// </summary>
public class SqlOnFhirTestCase
{
    /// <summary>
    /// Test case title
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Test tags (e.g., "shareable")
    /// </summary>
#pragma warning disable CA1002, CA2227 // Do not expose generic lists; Collections properties should be read-only
    public required List<string> Tags { get; set; }
#pragma warning restore CA1002, CA2227

    /// <summary>
    /// The ViewDefinition as ISourceNavigator for direct parsing
    /// </summary>
    public ISourceNavigator? ViewNode { get; set; }

    /// <summary>
    /// Expected result rows
    /// </summary>
#pragma warning disable CA1002, CA2227 // Do not expose generic lists; Collections properties should be read-only
    public required List<Dictionary<string, object?>> ExpectedRows { get; set; }
#pragma warning restore CA1002, CA2227

    /// <summary>
    /// Whether this test is expected to throw an error
    /// </summary>
    public bool ExpectError { get; set; }

    /// <summary>
    /// Expected column order (if specified in test)
    /// </summary>
#pragma warning disable CA1002, CA2227 // Do not expose generic lists; Collections properties should be read-only
    public List<string> ExpectedColumns { get; set; } = new();
#pragma warning restore CA1002, CA2227
}
