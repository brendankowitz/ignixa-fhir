using Ignixa.FhirPath.Tests.TestHelpers;

namespace Ignixa.FhirPath.Tests;

public class OfficialTestSuiteRunnerPredicateTests
{
    private readonly OfficialTestSuiteRunner _runner = new(new NullTestOutputHelper());

    [Fact]
    public void GivenR5OfficialTestCases_WhenEnumerating_ThenIncludesPredicateCases()
    {
        var testCases = OfficialTestSuiteRunner.GetR5TestCases()
            .Select(testCaseData => Assert.IsType<FhirPathTestCase>(testCaseData[0]))
            .ToList();

        testCases.ShouldContain(testCase => testCase.Name == "testPatientHasBirthDate");
    }

    [Fact]
    public void GivenPredicateBirthDateTest_WhenRunningR5OfficialSuite_ThenPasses()
    {
        var testCase = OfficialTestSuiteRunner.GetR5TestCases()
            .Select(testCaseData => Assert.IsType<FhirPathTestCase>(testCaseData[0]))
            .Single(testCase => testCase.Name == "testPatientHasBirthDate");

        _runner.OfficialTestSuite_R5(testCase);
    }

    [Fact]
    public void GivenPredicateExpressionReturnsEmpty_WhenRunningR5OfficialSuite_ThenEvaluatesFalse()
    {
        var testCase = CreatePredicateTestCase("testPredicateEmpty", "deceased", expectedValue: false);

        _runner.OfficialTestSuite_R5(testCase);
    }

    [Fact]
    public void GivenPredicateExpressionReturnsBooleanFalse_WhenRunningR5OfficialSuite_ThenEvaluatesFalse()
    {
        var testCase = CreatePredicateTestCase("testPredicateBooleanFalse", "1 = 2", expectedValue: false);

        _runner.OfficialTestSuite_R5(testCase);
    }

    [Fact]
    public void GivenPredicateExpressionReturnsBooleanTrue_WhenRunningR5OfficialSuite_ThenEvaluatesTrue()
    {
        var testCase = CreatePredicateTestCase("testPredicateBooleanTrue", "1 = 1", expectedValue: true);

        _runner.OfficialTestSuite_R5(testCase);
    }

    [Fact]
    public void GivenPredicateExpressionReturnsMultipleItems_WhenRunningR5OfficialSuite_ThenEvaluatesTrue()
    {
        var testCase = CreatePredicateTestCase("testPredicateMultipleItems", "(1 | 2)", expectedValue: true);

        _runner.OfficialTestSuite_R5(testCase);
    }

    private static FhirPathTestCase CreatePredicateTestCase(string name, string expression, bool expectedValue)
    {
        return new FhirPathTestCase(
            Name: name,
            GroupName: "predicate-tests",
            Expression: expression,
            InputFile: null,
            ExpectedOutputs: [new ExpectedOutput("boolean", expectedValue ? "true" : "false")],
            IsInvalidTest: false,
            InvalidType: null,
            Ordered: true,
            Predicate: true,
            Description: null,
            Mode: null);
    }
}
