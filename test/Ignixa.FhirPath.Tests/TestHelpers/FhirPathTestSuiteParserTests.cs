using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.TestHelpers;

public class FhirPathTestSuiteParserTests
{
    private readonly string _testSuiteFilePath;

    public FhirPathTestSuiteParserTests()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        _testSuiteFilePath = Path.Combine(projectRoot, "TestData", "fhir-test-cases", "r4", "fhirpath", "tests-fhir-r4.xml");
    }

    [Fact]
    public void GivenTestSuiteFile_WhenParsing_ThenReturnsTestCases()
    {
        var testCases = FhirPathTestSuiteParser.ParseTestSuite(_testSuiteFilePath);

        testCases.ShouldNotBeNull();
        testCases.Count.ShouldBeGreaterThan(900);
    }

    [Fact]
    public void GivenTestSuiteFile_WhenParsing_ThenExtractsGroupNames()
    {
        var testCases = FhirPathTestSuiteParser.ParseTestSuite(_testSuiteFilePath);

        var groupNames = testCases.Select(tc => tc.GroupName).Distinct().ToList();

        groupNames.ShouldContain("comments");
        groupNames.ShouldContain("testBasics");
        groupNames.ShouldContain("testEquality");
    }

    [Fact]
    public void GivenTestSuiteFile_WhenParsing_ThenExtractsTestProperties()
    {
        var testCases = FhirPathTestSuiteParser.ParseTestSuite(_testSuiteFilePath);

        var simpleTest = testCases.First(tc => tc.Name == "testSimple");

        simpleTest.GroupName.ShouldBe("testBasics");
        simpleTest.Expression.ShouldBe("name.given");
        simpleTest.InputFile.ShouldBe("patient-example.xml");
        simpleTest.ExpectedOutputs.Count.ShouldBe(5);
        simpleTest.ExpectedOutputs[0].Type.ShouldBe("string");
        simpleTest.ExpectedOutputs[0].Value.ShouldBe("Peter");
        simpleTest.IsInvalidTest.ShouldBeFalse();
        simpleTest.Ordered.ShouldBeTrue();
        simpleTest.Predicate.ShouldBeFalse();
    }

    [Fact]
    public void GivenTestSuiteFile_WhenParsing_ThenExtractsInvalidTests()
    {
        var testCases = FhirPathTestSuiteParser.ParseTestSuite(_testSuiteFilePath);

        var invalidTest = testCases.First(tc => tc.Name == "testComment7");

        invalidTest.IsInvalidTest.ShouldBeTrue();
        invalidTest.InvalidType.ShouldBe("syntax");
        invalidTest.Expression.ShouldBe("2 + 2 /");
    }

    [Fact]
    public void GivenTestSuiteFile_WhenParsing_ThenExtractsPredicateTests()
    {
        var testCases = FhirPathTestSuiteParser.ParseTestSuite(_testSuiteFilePath);

        var predicateTest = testCases.First(tc => tc.Name == "testPatientHasBirthDate");

        predicateTest.Predicate.ShouldBeTrue();
        predicateTest.ExpectedOutputs[0].Type.ShouldBe("boolean");
        predicateTest.ExpectedOutputs[0].Value.ShouldBe("true");
    }

    [Fact]
    public void GivenTestSuiteFile_WhenParsing_ThenHandlesEmptyResults()
    {
        var testCases = FhirPathTestSuiteParser.ParseTestSuite(_testSuiteFilePath);

        var emptyResultTest = testCases.First(tc => tc.Name == "testSimpleNone");

        emptyResultTest.ExpectedOutputs.Count.ShouldBe(0);
        emptyResultTest.Expression.ShouldBe("name.suffix");
    }

    [Fact]
    public void GivenInvalidFilePath_WhenParsing_ThenThrowsFileNotFoundException()
    {
        Should.Throw<FileNotFoundException>(() =>
            FhirPathTestSuiteParser.ParseTestSuite("nonexistent.xml"));
    }

    [Fact]
    public void GivenNullFilePath_WhenParsing_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            FhirPathTestSuiteParser.ParseTestSuite(null!));
    }
}
