using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.TestHelpers;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;

namespace Ignixa.FhirPath.Tests;

public class OfficialTestSuiteRunner
{
    private static readonly string _projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private static readonly Lazy<IReadOnlyList<FhirPathTestCase>> _r4TestCases = new(() => LoadTestCases("r4"));
    private static readonly Lazy<IReadOnlyList<FhirPathTestCase>> _r4bTestCases = new(() => LoadTestCases("r4b"));
    private static readonly Lazy<IReadOnlyList<FhirPathTestCase>> _r5TestCases = new(() => LoadTestCases("r5"));

    // Functions that are not yet implemented. Tests using these are skipped to focus on supported functionality.
    // Type introspection: is(), conformsTo()
    // Collection operations: aggregate()
    // Variable definition: defineVariable() (FHIRPath 2.0 feature)
    // Terminology services: %terminologies.expand, validateVS(), translate()
    // Precision function: precision()

    private static readonly FrozenSet<string> _unsupportedFunctions = new[]
    {
        "conformsTo(",
        "%terminologies",
        "validateVS(",
        "translate("
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Scopes that are not yet implemented
    private static readonly FrozenSet<string> _unsupportedScopes = FrozenSet<string>.Empty;

    // Matches the 'is' operator when used with type names (e.g., "value is Quantity")
    // This is distinct from .is() function calls which are caught by _unsupportedFunctions
    private static readonly Regex _isOperatorPattern = new(@"\bis\s+\w", RegexOptions.Compiled | RegexOptions.IgnoreCase);


    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private static IReadOnlyList<FhirPathTestCase> LoadTestCases(string version)
    {
        var testSuiteFilePath = Path.Combine(_projectRoot, "TestData", "fhir-test-cases", version, "fhirpath", $"tests-fhir-{version}.xml");

        if (!File.Exists(testSuiteFilePath))
        {
            throw new FileNotFoundException($"Test suite file not found. Ensure FHIR test cases are downloaded: {testSuiteFilePath}");
        }

        return FhirPathTestSuiteParser.ParseTestSuite(testSuiteFilePath);
    }

    public static IEnumerable<object[]> GetR4TestCases() => GetTestCasesForVersion("R4", _r4TestCases);
    public static IEnumerable<object[]> GetR4BTestCases() => GetTestCasesForVersion("R4B", _r4bTestCases);
    public static IEnumerable<object[]> GetR5TestCases() => GetTestCasesForVersion("R5", _r5TestCases);

    private static IEnumerable<object[]> GetTestCasesForVersion(string versionLabel, Lazy<IReadOnlyList<FhirPathTestCase>> testCasesLazy)
    {
        var testCases = testCasesLazy.Value;

        var filteredTests = testCases
            .Where(tc => !tc.IsInvalidTest)
            .Where(tc => tc.InputFile is not null)
            .Where(tc => !tc.Predicate)
            .Where(tc => !ShouldSkipTest(tc));

        var totalTests = testCases.Count;
        var afterBasicFiltering = testCases.Count(tc => !tc.IsInvalidTest && tc.InputFile is not null && !tc.Predicate);
        var afterSkipFiltering = filteredTests.Count();
        var skippedCount = afterBasicFiltering - afterSkipFiltering;

        Console.WriteLine($"[OfficialTestSuite-{versionLabel}] Total tests: {totalTests}, After basic filtering: {afterBasicFiltering}, Skipped (unsupported): {skippedCount}, Running: {afterSkipFiltering}");

        foreach (var testCase in filteredTests)
        {
            yield return [testCase];
        }
    }

    private static bool ShouldSkipTest(FhirPathTestCase testCase)
    {
        if (_isOperatorPattern.IsMatch(testCase.Expression))
        {
            return true;
        }

        foreach (var unsupportedFunction in _unsupportedFunctions)
        {
            if (testCase.Expression.Contains(unsupportedFunction, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var unsupportedScope in _unsupportedScopes)
        {
            if (testCase.Expression.Contains(unsupportedScope, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [SkippableTheory]
    [MemberData(nameof(GetR4TestCases))]
    [Trait("Category", "OfficialTestSuite")]
    [Trait("FhirVersion", "R4")]
    public void OfficialTestSuite_R4(FhirPathTestCase testCase)
    {
        RunTestCase(testCase, FhirVersion.R4);
    }

    [SkippableTheory]
    [MemberData(nameof(GetR4BTestCases))]
    [Trait("Category", "OfficialTestSuite")]
    [Trait("FhirVersion", "R4B")]
    public void OfficialTestSuite_R4B(FhirPathTestCase testCase)
    {
        RunTestCase(testCase, FhirVersion.R4B);
    }

    [SkippableTheory]
    [MemberData(nameof(GetR5TestCases))]
    [Trait("Category", "OfficialTestSuite")]
    [Trait("FhirVersion", "R5")]
    public void OfficialTestSuite_R5(FhirPathTestCase testCase)
    {
        RunTestCase(testCase, FhirVersion.R5);
    }

    private void RunTestCase(FhirPathTestCase testCase, FhirVersion fhirVersion)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(testCase.InputFile);

        var versionString = fhirVersion switch
        {
            FhirVersion.R4 => "r4",
            FhirVersion.R4B => "r4b",
            FhirVersion.R5 => "r5",
            _ => throw new ArgumentOutOfRangeException(nameof(fhirVersion))
        };

        var examplesDirectory = Path.Combine(_projectRoot, "TestData", "fhir-test-cases", versionString, "examples");
        var inputFilePath = Path.Combine(examplesDirectory, testCase.InputFile);

        if (!File.Exists(inputFilePath))
        {
            Skip.If(true, $"Input file not found: {testCase.InputFile}");
        }

        var schemaProvider = fhirVersion.GetSchemaProvider();
        var resourceJson = FhirXmlToJsonConverter.LoadResourceAsJson(inputFilePath);
        var resource = ResourceJsonNode.Parse(resourceJson);
        var element = resource.ToElement(schemaProvider);

        // Act
        Expression expression;
        try
        {
            expression = _parser.Parse(testCase.Expression);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse FHIRPath expression '{testCase.Expression}' in test '{testCase.Name}' (group: {testCase.GroupName})", ex);
        }

        IEnumerable<IElement> results;
        try
        {
            results = _evaluator.Evaluate(element, expression, new EvaluationContext());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to evaluate FHIRPath expression '{testCase.Expression}' in test '{testCase.Name}' (group: {testCase.GroupName}, input: {testCase.InputFile})", ex);
        }

        // Assert
        var resultList = results.ToList();
        ValidateResults(testCase, resultList);
    }

    private static void ValidateResults(FhirPathTestCase testCase, List<IElement> actualResults)
    {
        var expectedCount = testCase.ExpectedOutputs.Count;
        var actualCount = actualResults.Count;

        if (actualCount != expectedCount)
        {
            var message = $"""
                Result count mismatch in test '{testCase.Name}' (group: {testCase.GroupName})
                Expression: {testCase.Expression}
                Input file: {testCase.InputFile}
                Expected {expectedCount} result(s), but got {actualCount}
                Expected outputs: {FormatExpectedOutputs(testCase.ExpectedOutputs)}
                Actual outputs: {FormatActualOutputs(actualResults)}
                """;
            throw new InvalidOperationException(message);
        }

        for (var i = 0; i < expectedCount; i++)
        {
            var expected = testCase.ExpectedOutputs[i];
            var actual = actualResults[i];

            ValidateResult(testCase, expected, actual, i);
        }
    }

    private static void ValidateResult(FhirPathTestCase testCase, ExpectedOutput expected, IElement actual, int index)
    {
        var expectedType = expected.Type;
        var expectedValue = expected.Value;

        var actualValue = actual.Value;
        var actualType = InferFhirPathType(actualValue);

        // If the value is a string but the element metadata says it's a temporal type, trust the metadata
        // This handles the case where the model returns raw values (no @ prefix)
        if (actualType == "string" && 
            (actual.InstanceType == "date" || actual.InstanceType == "dateTime" || actual.InstanceType == "time" || actual.InstanceType == "instant"))
        {
            actualType = actual.InstanceType;
        }

        if (!TypesMatch(expectedType, actualType, actualValue))
        {
            var message = $"""
                Type mismatch in test '{testCase.Name}' (group: {testCase.GroupName}) at output index {index}
                Expression: {testCase.Expression}
                Input file: {testCase.InputFile}
                Expected type: {expectedType}
                Actual type: {actualType}
                Expected value: {expectedValue}
                Actual value: {actualValue ?? "(null)"}
                """;
            throw new InvalidOperationException(message);
        }

        if (!ValuesMatch(expectedValue, actualValue, expectedType))
        {
            var message = $"""
                Value mismatch in test '{testCase.Name}' (group: {testCase.GroupName}) at output index {index}
                Expression: {testCase.Expression}
                Input file: {testCase.InputFile}
                Expected: {expectedValue} (type: {expectedType})
                Actual: {actualValue ?? "(null)"} (type: {actualType})
                """;
            throw new InvalidOperationException(message);
        }
    }

    private static string InferFhirPathType(object? value)
    {
        return value switch
        {
            null => "null",
            bool => "boolean",
            int => "integer",
            long => "integer",
            decimal => "decimal",
            double => "decimal",
            string str when str.StartsWith('@') => ParseFhirPathTypePrefix(str),
            string => "string",
            _ => value.GetType().Name
        };
    }

    private static string ParseFhirPathTypePrefix(string value)
    {
        if (value.StartsWith("@T", StringComparison.Ordinal))
        {
            return "time";
        }
        if (value.StartsWith('@') && value.Length > 1)
        {
            if (value.Contains('T', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal))
            {
                return "dateTime";
            }
            return "date";
        }
        return "string";
    }

    private static bool TypesMatch(string expectedType, string actualType, object? actualValue)
    {
        if (expectedType == actualType)
        {
            return true;
        }

        if (expectedType == "code" && actualType == "string")
        {
            return true;
        }

        if (expectedType == "string" && actualType == "code")
        {
            return true;
        }

        if (expectedType == "integer" && actualType == "decimal")
        {
            if (actualValue is decimal decValue && decValue == Math.Floor(decValue))
            {
                return true;
            }
        }

        if (expectedType == "decimal" && actualType == "integer")
        {
            return true;
        }

        if ((expectedType == "date" || expectedType == "dateTime") && actualType == "string" && actualValue is string str && str.StartsWith('@'))
        {
            return true;
        }

        return false;
    }

    private static bool ValuesMatch(string expectedValue, object? actualValue, string expectedType)
    {
        if (actualValue is null)
        {
            return string.IsNullOrEmpty(expectedValue);
        }

        var actualStr = actualValue.ToString();
        if (actualStr is null)
        {
            return string.IsNullOrEmpty(expectedValue);
        }

        if (expectedType is "date" or "dateTime" or "time")
        {
            return NormalizeTemporalValue(expectedValue) == NormalizeTemporalValue(actualStr);
        }

        if (expectedType == "boolean")
        {
            return string.Equals(expectedValue, actualStr, StringComparison.OrdinalIgnoreCase);
        }

        if (expectedType is "integer" or "decimal")
        {
            if (decimal.TryParse(expectedValue, out var expectedDecimal) && decimal.TryParse(actualStr, out var actualDecimal))
            {
                return expectedDecimal == actualDecimal;
            }
        }

        return string.Equals(expectedValue, actualStr, StringComparison.Ordinal);
    }

    private static string NormalizeTemporalValue(string value)
    {
        return value.TrimStart('@');
    }

    private static string FormatExpectedOutputs(IReadOnlyList<ExpectedOutput> outputs)
    {
        if (outputs.Count == 0)
        {
            return "(empty collection)";
        }

        return string.Join(", ", outputs.Select(o => $"{o.Value} ({o.Type})"));
    }

    private static string FormatActualOutputs(List<IElement> results)
    {
        if (results.Count == 0)
        {
            return "(empty collection)";
        }

        return string.Join(", ", results.Select(r => $"{r.Value ?? "(null)"} ({InferFhirPathType(r.Value)})"));
    }
}
