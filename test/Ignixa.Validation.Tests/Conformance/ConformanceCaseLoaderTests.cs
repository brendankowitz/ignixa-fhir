// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// Unit tests for <see cref="ConformanceCaseAnalysis"/>: the pure filtering and outcome-counting
/// logic that gates which manifest entries make it into the conformance sample. These tests run
/// against synthetic <see cref="JsonElement"/>/JSON fixtures and never touch the vendored test-case
/// directory on disk, so the counting/filter logic is verified independently of the conformance
/// baseline's own pass rate.
/// </summary>
public class ConformanceCaseLoaderTests
{
    private static ConformanceTestCase CleanR4Case(
        string? version = "4.0",
        bool useTest = true,
        string? file = "case.json",
        List<string>? packages = null,
        List<string>? supporting = null,
        List<string>? profiles = null,
        JsonElement? profile = null,
        JsonElement? logical = null) => new()
    {
        Name = "clean-base-case",
        File = file,
        Version = version,
        UseTest = useTest,
        Packages = packages,
        Supporting = supporting,
        Profiles = profiles,
        Profile = profile,
        Logical = logical,
    };

    [Theory]
    [InlineData("""{"issue":[]}""", 0)]
    [InlineData("""{"issue":[{"severity":"error"}]}""", 1)]
    [InlineData("""{"issue":[{"severity":"fatal"}]}""", 1)]
    [InlineData("""{"issue":[{"severity":"warning"}]}""", 0)]
    [InlineData("""{"issue":[{"severity":"information"}]}""", 0)]
    [InlineData("""{"issue":[{"severity":"error"},{"severity":"warning"},{"severity":"fatal"}]}""", 2)]
    [InlineData("""{"unrelated":true}""", 0)]
    [InlineData("""{"issue":"not-an-array"}""", 0)]
    public void GivenOperationOutcomeJson_WhenCountingErrorIssues_ThenReturnsExpectedCount(string json, int expected)
    {
        using var doc = JsonDocument.Parse(json);

        var result = ConformanceCaseAnalysis.CountErrorIssues(doc.RootElement);

        result.ShouldBe(expected);
    }

    [Fact]
    public void GivenInlineOutcomeWithErrorCountProperty_WhenCounting_ThenReturnsThatCount()
    {
        using var doc = JsonDocument.Parse("""{"errorCount": 3}""");

        var result = ConformanceCaseAnalysis.TryCountErrorsInInlineOutcome(doc.RootElement);

        result.ShouldBe(3);
    }

    [Fact]
    public void GivenInlineOutcomeWithNestedOutcomeObject_WhenCounting_ThenCountsErrorIssuesWithin()
    {
        using var doc = JsonDocument.Parse("""
        {"outcome": {"issue": [{"severity":"error"}, {"severity":"warning"}]}}
        """);

        var result = ConformanceCaseAnalysis.TryCountErrorsInInlineOutcome(doc.RootElement);

        result.ShouldBe(1);
    }

    [Fact]
    public void GivenInlineOutcomeWithNeitherRecognizedShape_WhenCounting_ThenReturnsNull()
    {
        using var doc = JsonDocument.Parse("""{"unrelated": true}""");

        var result = ConformanceCaseAnalysis.TryCountErrorsInInlineOutcome(doc.RootElement);

        result.ShouldBeNull();
    }

    [Fact]
    public void GivenWellFormedOutcomeFileContent_WhenCounting_ThenReturnsErrorCount()
    {
        const string content =
            """{"resourceType":"OperationOutcome","issue":[{"severity":"error"},{"severity":"information"}]}""";

        var result = ConformanceCaseAnalysis.TryCountErrorsInOutcomeContent(content);

        result.ShouldBe(1);
    }

    [Fact]
    public void GivenMalformedOutcomeFileContent_WhenCounting_ThenReturnsNullInsteadOfThrowing()
    {
        const string content = "{ this is not valid json";

        var result = ConformanceCaseAnalysis.TryCountErrorsInOutcomeContent(content);

        result.ShouldBeNull();
    }

    [Fact]
    public void GivenR4CaseWithExistingJsonFile_WhenFiltering_ThenIsCleanBase()
    {
        var result = ConformanceCaseAnalysis.IsR4CleanBase(CleanR4Case(), _ => true);

        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenVersionOtherThanR4_WhenFiltering_ThenExcluded()
    {
        var testCase = CleanR4Case(version: "5.0");

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenVersionAbsent_WhenFiltering_ThenExcluded()
    {
        var testCase = CleanR4Case(version: null);

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenUseTestFalse_WhenFiltering_ThenExcluded()
    {
        var testCase = CleanR4Case(useTest: false);

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenFileMissing_WhenFiltering_ThenExcluded()
    {
        var testCase = CleanR4Case(file: null);

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenFileNotJsonExtension_WhenFiltering_ThenExcluded()
    {
        var testCase = CleanR4Case(file: "case.xml");

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenPackagesPresent_WhenFiltering_ThenExcluded()
    {
        var testCase = CleanR4Case(packages: ["some-ig#1.0.0"]);

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenSupportingResourcesPresent_WhenFiltering_ThenExcluded()
    {
        var testCase = CleanR4Case(supporting: ["Patient/example.json"]);

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenProfilesPresent_WhenFiltering_ThenExcluded()
    {
        var testCase = CleanR4Case(profiles: ["http://example.org/StructureDefinition/foo"]);

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenExplicitProfilePresent_WhenFiltering_ThenExcluded()
    {
        using var doc = JsonDocument.Parse("""{"reference":"http://example.org/StructureDefinition/foo"}""");
        var testCase = CleanR4Case(profile: doc.RootElement);

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenLogicalConfigurationPresent_WhenFiltering_ThenExcluded()
    {
        using var doc = JsonDocument.Parse("""{"source":"logical.json"}""");
        var testCase = CleanR4Case(logical: doc.RootElement);

        var result = ConformanceCaseAnalysis.IsR4CleanBase(testCase, _ => true);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenFileDoesNotExistOnDisk_WhenFiltering_ThenExcluded()
    {
        var result = ConformanceCaseAnalysis.IsR4CleanBase(CleanR4Case(), _ => false);

        result.ShouldBeFalse();
    }
}
