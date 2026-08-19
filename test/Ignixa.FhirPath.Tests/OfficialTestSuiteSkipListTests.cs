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
    /// <summary>
    /// Checks every deferred entry still names a real, still-invalid official case.
    /// </summary>
    /// <remarks>
    /// A <see cref="TheoryAttribute"/> over the deferral list would give one case per entry and nicer
    /// failure isolation, but xunit fails a theory whose data source is empty ("No data found"). The list
    /// is currently empty because every deferral has been closed, so the guard has to survive its own
    /// success - a guard that breaks when there is nothing left to guard would push the next person to
    /// delete it. Failures are therefore accumulated and reported together, which keeps the per-entry
    /// diagnostic that the theory form gave.
    /// </remarks>
    [Fact]
    public void GivenTheDeferredInvalidCases_WhenLookedUpInTheOfficialSuites_ThenEachExistsAndIsMarkedInvalid()
    {
        // Arrange
        var allCases = AllTestCases().ToList();

        // Act
        var stale = new List<string>();
        foreach (var testName in OfficialTestSuiteRunner.DeferredInvalidCaseNames)
        {
            var matches = allCases.Where(tc => tc.Name == testName).ToList();

            if (matches.Count == 0)
            {
                stale.Add($"'{testName}' is deferred but no official R4/R4B/R5 test has that name - the entry is stale and should be removed.");
            }
            else if (!matches.Any(tc => tc.IsInvalidTest))
            {
                stale.Add($"'{testName}' is deferred as an unsignalled invalid case but no version marks it invalid - the entry no longer describes a real gap.");
            }
        }

        // Assert
        stale.ShouldBeEmpty(string.Join(Environment.NewLine, stale));
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
