/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Guards the deferral list in OfficialTestSuiteRunner.
 *
 * Skipping an official invalid-expression case reports as a pass in xunit v2, so the list itself needs
 * teeth: an entry naming a test that no longer exists upstream would silently defer nothing while still
 * reading as a documented gap.
 */

using Ignixa.FhirPath.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests;

public class OfficialTestSuiteSkipListTests
{
    public static TheoryData<string> DeferredCaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in OfficialTestSuiteRunner.DeferredInvalidCaseNames)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DeferredCaseNames))]
    public void GivenADeferredInvalidCase_WhenLookedUpInTheOfficialSuites_ThenItExistsAndIsMarkedInvalid(string testName)
    {
        // Arrange
        var matches = AllTestCases().Where(tc => tc.Name == testName).ToList();

        // Act
        var invalidMatches = matches.Where(tc => tc.IsInvalidTest).ToList();

        // Assert
        matches.ShouldNotBeEmpty($"'{testName}' is deferred but no official R4/R4B/R5 test has that name - the entry is stale and should be removed.");
        invalidMatches.ShouldNotBeEmpty($"'{testName}' is deferred as an unsignalled invalid case but no version marks it invalid - the entry no longer describes a real gap.");
    }

    [Fact]
    public void GivenTheDeferralList_WhenInspected_ThenEveryEntryCarriesAReason()
    {
        // Arrange & Act
        var reasons = OfficialTestSuiteRunner.DeferredInvalidCaseReasons;

        // Assert
        reasons.ShouldAllBe(entry => !string.IsNullOrWhiteSpace(entry.Value));
        reasons.ShouldAllBe(entry => entry.Value.Length > 40);
    }

    private static IEnumerable<FhirPathTestCase> AllTestCases()
    {
        foreach (var version in new[] { "r4", "r4b", "r5" })
        {
            var path = Path.Combine(
                OfficialTestSuiteRunner.ProjectRoot,
                "TestData",
                "fhir-test-cases",
                version,
                "fhirpath",
                $"tests-fhir-{version}.xml");

            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var testCase in FhirPathTestSuiteParser.ParseTestSuite(path))
            {
                yield return testCase;
            }
        }
    }
}
