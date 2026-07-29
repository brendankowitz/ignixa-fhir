/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

public class FmlManifestLoaderTests
{
    // Re-derives the URL last segment in test code; intentionally does not
    // delegate to production code to avoid circular trust in the stale-key test.
    private static string LastSegment(string name) =>
        name.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    public void GivenManifest_WhenLoading_ThenReturnsAllTenCases(string version)
    {
        var cases = FmlManifestLoader.Load(version);

        cases.Count.ShouldBe(10, $"{version}: manifest yielded {cases.Count} cases");
        cases.ShouldContain(c =>
            c.MapFile == "qr2pat-gender.map" &&
            c.SourceFile == "qr.json" &&
            c.OutputFile == "qr2pat-gender-res.json");
    }

    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    public void GivenManifest_WhenFilteringExcluded_ThenExactlyInScopeCasesRemain(string version)
    {
        var cases = FmlManifestLoader.Load(version);
        var supported = cases
            .Where(c => !FmlOracleExclusions.IsExcluded(c.Name))
            .ToList();

        var expectedSegments = new List<string>
        {
            "qr2patassignment",
            "qr2patgender",
            "qr2pathumannametwice",
            "qr2pathumannameshared",
            "reference",
            "qr2pat-gender-conformstoqr"
        };

        var actualSegments = supported
            .Select(c => LastSegment(c.Name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var expectedSorted = expectedSegments
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        actualSegments.ShouldBe(expectedSorted);
    }

    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    public void GivenExclusionList_WhenCheckingAgainstManifest_ThenAllKeysMatchActualCases(string version)
    {
        var cases = FmlManifestLoader.Load(version);
        var caseSegments = cases
            .Select(c => LastSegment(c.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Guard against the vacuous pass: if the exclusion list were emptied
        // entirely the loop below runs zero times and passes, defeating its purpose.
        FmlOracleExclusions.All.ShouldNotBeEmpty();

        foreach (var key in FmlOracleExclusions.All.Keys)
        {
            caseSegments.ShouldContain(key);
        }
    }

    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    public void GivenManifest_WhenCheckingOutputExtensions_ThenXmlMeansExcludedAndJsonMeansInScope(string version)
    {
        var cases = FmlManifestLoader.Load(version);

        cases.Count(c => FmlOracleExclusions.IsExcluded(c.Name)).ShouldBe(4);
        cases.Count(c => !FmlOracleExclusions.IsExcluded(c.Name)).ShouldBe(6);

        foreach (var c in cases.Where(c => FmlOracleExclusions.IsExcluded(c.Name)))
        {
            c.OutputFile.ShouldEndWith(".xml");
        }

        foreach (var c in cases.Where(c => !FmlOracleExclusions.IsExcluded(c.Name)))
        {
            c.OutputFile.ShouldEndWith(".json");
        }
    }

    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    public void GivenManifest_WhenCheckingRationales_ThenExcludedCasesHaveRationaleAndInScopeDoNot(string version)
    {
        var cases = FmlManifestLoader.Load(version);

        cases.Count(c => FmlOracleExclusions.IsExcluded(c.Name)).ShouldBe(4);
        cases.Count(c => !FmlOracleExclusions.IsExcluded(c.Name)).ShouldBe(6);

        foreach (var c in cases.Where(c => FmlOracleExclusions.IsExcluded(c.Name)))
        {
            var rationale = FmlOracleExclusions.RationaleFor(c.Name);
            rationale.ShouldNotBeNull();
            rationale.Trim().ShouldNotBeEmpty();
        }

        foreach (var c in cases.Where(c => !FmlOracleExclusions.IsExcluded(c.Name)))
        {
            FmlOracleExclusions.RationaleFor(c.Name).ShouldBeNull();
        }
    }

    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    public void GivenManifest_WhenLoading_ThenVersionIsCarriedOnAllCases(string version)
    {
        var cases = FmlManifestLoader.Load(version);

        foreach (var c in cases)
        {
            c.Version.ShouldBe(version);
            c.ToString().ShouldStartWith($"{version}/");
        }
    }

    [Fact]
    public void GivenMissingManifest_WhenLoading_ThenThrowsFileNotFoundException()
    {
        Should.Throw<FileNotFoundException>(() => FmlManifestLoader.Load("nonexistent-version"));
    }
}
