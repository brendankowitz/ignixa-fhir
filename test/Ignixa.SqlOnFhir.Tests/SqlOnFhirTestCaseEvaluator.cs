/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Single source of truth for evaluating one official SQL on FHIR v2 test case:
 * loads resources, validates schema, evaluates the view, and compares rows, returning
 * a pass/fail-with-reason outcome instead of asserting.
 */

using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using System.Globalization;
using System.Text.Json;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Evaluates a single SQL on FHIR conformance case and reports whether it passed and why not.
/// This is the single source of truth for pass/fail used by both the conformance runner
/// (for the build gate) and the report collector (for the emitted report).
/// </summary>
public static class SqlOnFhirTestCaseEvaluator
{
    /// <summary>
    /// Runs one test case and returns its outcome. The caller must ensure
    /// <paramref name="testCase"/>.ViewNode is non-null; a null view is a skip handled by the runner.
    /// </summary>
    public static SqlOnFhirTestCaseOutcome Run(SqlOnFhirTestFile testFile, SqlOnFhirTestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testFile);
        ArgumentNullException.ThrowIfNull(testCase);

        if (testCase.ViewNode is null)
        {
            return SqlOnFhirTestCaseOutcome.Skipped();
        }

        var evaluator = new SqlOnFhirEvaluator();
        var schemaEvaluator = new SqlOnFhirSchemaEvaluator();

        var fhirVersion = testFile.FhirVersions.FirstOrDefault() ?? "4.0.1";
        var resourceType = testCase.ViewNode.Children("resource").FirstOrDefault()?.Text ?? "Unknown";
        var resources = LoadResources(testFile.Resources, resourceType, fhirVersion);

        return testCase.ExpectError
            ? EvaluateExpectError(testCase, resources, evaluator)
            : EvaluateExpectRows(testCase, resources, evaluator, schemaEvaluator);
    }

    private static SqlOnFhirTestCaseOutcome EvaluateExpectError(
        SqlOnFhirTestCase testCase,
        List<IElement> resources,
        SqlOnFhirEvaluator evaluator)
    {
        var exceptionThrown = false;
        try
        {
            _ = ViewDefinitionExpressionParser.Parse(testCase.ViewNode!);

            foreach (var resource in resources)
            {
                _ = evaluator.Evaluate(testCase.ViewNode!, resource).ToList();
            }
        }
        catch (Exception ex) when (ex is not (NullReferenceException or OutOfMemoryException or StackOverflowException))
        {
            exceptionThrown = true;
        }

        return exceptionThrown
            ? SqlOnFhirTestCaseOutcome.Pass()
            : SqlOnFhirTestCaseOutcome.Fail("Expected an error but none was thrown");
    }

    private static SqlOnFhirTestCaseOutcome EvaluateExpectRows(
        SqlOnFhirTestCase testCase,
        List<IElement> resources,
        SqlOnFhirEvaluator evaluator,
        SqlOnFhirSchemaEvaluator schemaEvaluator)
    {
        var viewDefinitionExpression = ViewDefinitionExpressionParser.Parse(testCase.ViewNode!);

        var expectedColumns = GetExpectedColumns(testCase);
        if (expectedColumns.Count > 0)
        {
            var actualColumnNames = schemaEvaluator.GetSchema(viewDefinitionExpression)
                .Select(c => c.Name)
                .ToList();

            var schemaFailure = ValidateSchema(expectedColumns, actualColumnNames);
            if (schemaFailure is not null)
            {
                return SqlOnFhirTestCaseOutcome.Fail(schemaFailure);
            }
        }

        var actualResults = evaluator.EvaluateBatch(testCase.ViewNode!, resources).ToList();

        var rowFailure = CompareRows(testCase.ExpectedRows, actualResults, testCase.ExpectedColumns);
        return rowFailure is null
            ? SqlOnFhirTestCaseOutcome.Pass()
            : SqlOnFhirTestCaseOutcome.Fail(rowFailure);
    }

    private static string? ValidateSchema(List<string> expectedColumns, List<string> actualColumnNames)
    {
        var missing = expectedColumns.Except(actualColumnNames).ToList();
        var extra = actualColumnNames.Except(expectedColumns).ToList();

        if (expectedColumns.Count == actualColumnNames.Count && missing.Count == 0 && extra.Count == 0)
        {
            return null;
        }

        var message = $"Schema validation failed: expected [{string.Join(", ", expectedColumns)}], " +
                      $"got [{string.Join(", ", actualColumnNames)}]";

        if (missing.Count > 0)
        {
            message += $"; missing [{string.Join(", ", missing)}]";
        }

        if (extra.Count > 0)
        {
            message += $"; extra [{string.Join(", ", extra)}]";
        }

        return message;
    }

    private static List<string> GetExpectedColumns(SqlOnFhirTestCase testCase)
    {
        if (testCase.ExpectedColumns is { Count: > 0 })
        {
            return testCase.ExpectedColumns;
        }

        return testCase.ExpectedRows.Count > 0
            ? testCase.ExpectedRows[0].Keys.ToList()
            : [];
    }

    private static List<IElement> LoadResources(
        List<JsonElement> jsonResources,
        string resourceType,
        string fhirVersion)
    {
        var resources = new List<IElement>();
        var schemaProvider = GetSchemaProvider(fhirVersion);

        foreach (var jsonElement in jsonResources)
        {
            if (!jsonElement.TryGetProperty("resourceType", out var rtElement))
            {
                continue;
            }

            if (rtElement.GetString() != resourceType)
            {
                continue;
            }

            var resourceNode = ResourceJsonNode.Parse(jsonElement.GetRawText());
            resources.Add(resourceNode.ToElement(schemaProvider));
        }

        return resources;
    }

    private static ISchema GetSchemaProvider(string fhirVersion)
    {
        var spec = FhirSpecificationExtensions.FromVersionString(fhirVersion);
        return spec.GetSchemaProvider();
    }

    private static string? CompareRows(
        List<Dictionary<string, object?>> expected,
        List<Dictionary<string, object?>> actual,
        List<string>? expectedColumns)
    {
        if (expected.Count != actual.Count)
        {
            return $"Expected {expected.Count} rows but got {actual.Count}";
        }

        if (expectedColumns is { Count: > 0 })
        {
            var actualKeys = actual.FirstOrDefault()?.Keys.ToList() ?? [];
            if (expectedColumns.Count != actualKeys.Count)
            {
                return $"Expected {expectedColumns.Count} columns but got {actualKeys.Count}";
            }

            for (var i = 0; i < expectedColumns.Count; i++)
            {
                if (expectedColumns[i] != actualKeys[i])
                {
                    return $"Column order mismatch at index {i}: expected '{expectedColumns[i]}', got '{actualKeys[i]}'";
                }
            }
        }

        // The SQL on FHIR conformance suite compares result rows as an unordered multiset:
        // the upstream harness canonicalizes (sorts) both sides before comparing, so a
        // ViewDefinition's row order is not part of the contract. Match each expected row to a
        // distinct actual row regardless of position.
        var unmatchedActual = new List<Dictionary<string, object?>>(actual);
        for (var i = 0; i < expected.Count; i++)
        {
            var matchIndex = unmatchedActual.FindIndex(a => CompareRow(expected[i], a, i) is null);
            if (matchIndex < 0)
            {
                return CompareRow(expected[i], unmatchedActual.Count > 0 ? unmatchedActual[0] : [], i)
                    ?? $"Row {i}: no matching row found in actual results";
            }

            unmatchedActual.RemoveAt(matchIndex);
        }

        return null;
    }

    private static string? CompareRow(
        Dictionary<string, object?> expectedRow,
        Dictionary<string, object?> actualRow,
        int rowIndex)
    {
        if (expectedRow.Count != actualRow.Count)
        {
            return $"Row {rowIndex}: expected {expectedRow.Count} columns but got {actualRow.Count}";
        }

        foreach (var key in expectedRow.Keys)
        {
            if (!actualRow.TryGetValue(key, out var actualValue))
            {
                return $"Row {rowIndex}: missing column '{key}'";
            }

            var cellFailure = CompareCell(key, expectedRow[key], actualValue, rowIndex);
            if (cellFailure is not null)
            {
                return cellFailure;
            }
        }

        return null;
    }

    private static string? CompareCell(string key, object? expectedValue, object? actualValue, int rowIndex)
    {
        if (expectedValue is null && actualValue is null)
        {
            return null;
        }

        if (expectedValue is null || actualValue is null)
        {
            return $"Row {rowIndex} column '{key}': expected '{expectedValue ?? "null"}', got '{actualValue ?? "null"}'";
        }

        if (expectedValue is decimal or int or long or double)
        {
            var expectedNum = Convert.ToDecimal(expectedValue, CultureInfo.InvariantCulture);
            if (!TryToInvariantDecimal(actualValue, out var actualNum))
            {
                return $"Row {rowIndex} column '{key}': expected numeric '{expectedNum}', got '{actualValue}'";
            }

            return expectedNum == actualNum
                ? null
                : $"Row {rowIndex} column '{key}': expected '{expectedNum}', got '{actualNum}'";
        }

        var expectedStr = NormalizeTemporalValue(expectedValue.ToString()!);
        var actualStr = NormalizeTemporalValue(actualValue.ToString()!);

        if (IsDateOnly(expectedStr) && !IsDateOnly(actualStr))
        {
            actualStr = TruncateDateTimeToDate(actualStr);
        }

        return expectedStr == actualStr
            ? null
            : $"Row {rowIndex} column '{key}': expected '{expectedStr}', got '{actualStr}'";
    }

    private static bool TryToInvariantDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal d: result = d; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case double db: result = (decimal)db; return true;
            case string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0m;
                return false;
        }
    }

    private static bool IsDateOnly(string value)
    {
        return value.Length == 10 &&
               value[4] == '-' &&
               value[7] == '-' &&
               !value.Contains('T', StringComparison.Ordinal);
    }

    private static string NormalizeTemporalValue(string value)
    {
        if (value.StartsWith('T') && value.Length > 1 && char.IsDigit(value[1]))
        {
            return value[1..];
        }

        return value;
    }

    private static string TruncateDateTimeToDate(string value)
    {
        var tIndex = value.IndexOf('T', StringComparison.Ordinal);
        return tIndex > 0 ? value[..tIndex] : value;
    }
}
