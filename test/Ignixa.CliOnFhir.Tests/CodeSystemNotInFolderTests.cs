// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.CohortFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// V15: an empty search, or an empty code lookup, that names a code system (or a bare code) the folder's resources of that type
/// do not use says so. Each case is a fact read from the folder's codings; nothing is inferred from how a code looks. New at V15:
/// at 9f59d830 (V14) an empty answer carried no explanation, so every test that expects the issue fails there.
/// </summary>
public class CodeSystemNotInFolderTests
{
    private const string Icd10 = "http://hl7.org/fhir/sid/icd-10-cm";
    private const string Code = "code-system-not-in-folder";

    private static TestFolder SnomedConditions() => new TestFolder().Write("data.ndjson", Lines(
        Patient("p1", "1970-01-01", ""","gender":"female" """.TrimEnd()),
        Condition("c1", "p1"),
        Condition("c2", "p1", code: "44054006", clinical: "resolved")));

    private static IssueItem? Hint(ToolEnvelope<SearchFacts?> envelope) => envelope.Issues.SingleOrDefault(issue => issue.Code == Code);

    private static ToolEnvelope<SearchFacts?> Search(TestFolder folder, string type, string query, string? summary = null) =>
        ResourceSearcher.Search(SearchFixture.Open(folder), new SearchRequest(type, query, Summary: summary));

    [Fact]
    public void GivenASystemNoConditionIsCodedIn_WhenSearchedAndEmpty_ThenAnInformationIssueNamesTheSystemAndWhatConditionsUse()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<SearchFacts?> envelope = Search(folder, "Condition", $"code={Icd10}|I25", summary: "count");

        envelope.Facts!.Total.ShouldBe(0);
        IssueItem hint = Hint(envelope).ShouldNotBeNull();
        hint.Severity.ShouldBe("information");
        hint.Diagnostics.ShouldBe($"No Condition in this folder is coded in {Icd10}; Conditions here use {Sct}. Search the folder's codes by words (folder_code_systems text=...) and use those.");
    }

    [Fact]
    public void GivenAnUnusedSystemOrAUsedOne_WhenTheSearchMatches_ThenThereIsNoIssue()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<SearchFacts?> envelope = Search(folder, "Condition", $"code={Icd10}|I25,{Sct}|{Prediabetes}");

        envelope.Facts!.Total.ShouldBe(1);
        Hint(envelope).ShouldBeNull();
    }

    [Fact]
    public void GivenAUsedSystemWithACodeNoConditionHas_WhenSearchedAndEmpty_ThenThereIsNoIssue()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<SearchFacts?> envelope = Search(folder, "Condition", $"code={Sct}|1");

        envelope.Facts!.Total.ShouldBe(0);
        Hint(envelope).ShouldBeNull();
    }

    [Fact]
    public void GivenABareCodeNoCodingHolds_WhenSearchedAndEmpty_ThenTheIssueSaysSoAndListsTheSystemsTheTypeUses()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<SearchFacts?> envelope = Search(folder, "Condition", "code=I25", summary: "count");

        Hint(envelope).ShouldNotBeNull().Diagnostics.ShouldBe($"No Condition in this folder has the code I25 in any coding system; Conditions here use {Sct}. Search the folder's codes by words (folder_code_systems text=...) and use those.");
    }

    [Fact]
    public void GivenABareCodeSomeCodingHolds_WhenAnotherParameterMakesTheSearchEmpty_ThenThereIsNoIssue()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<SearchFacts?> envelope = Search(folder, "Condition", $"code=44054006&clinical-status=active");

        envelope.Facts!.Total.ShouldBe(0);
        Hint(envelope).ShouldBeNull();
    }

    [Theory]
    [InlineData("Patient", "identifier=urn:example:mrn|1")]
    [InlineData("Patient", "_id=nobody")]
    [InlineData("Patient", "gender=http://hl7.org/fhir/administrative-gender|male")]
    [InlineData("Patient", "gender=male")]
    [InlineData("Condition", "clinical-status=entered")]
    [InlineData("Condition", "verification-status=entered")]
    [InlineData("Condition", "code:text=nothing")]
    [InlineData("Condition", "subject:Patient.name=nobody")]
    public void GivenAParameterThatIsNotACodingLookup_WhenSearchedAndEmpty_ThenThereIsNoIssue(string type, string query)
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<SearchFacts?> envelope = Search(folder, type, query);

        envelope.Facts!.Total.ShouldBe(0);
        Hint(envelope).ShouldBeNull();
    }

    [Fact]
    public void GivenASystemOnlyAnotherTypeUses_WhenSearchedAndEmpty_ThenTheTypeSpecificIssueStillFires()
    {
        using TestFolder folder = SnomedConditions().Write("labs.ndjson", Lab("o1", "p1", A1c, "2026-01-01", 5m));

        ToolEnvelope<SearchFacts?> envelope = Search(folder, "Condition", $"code={Loinc}|{A1c}");

        Hint(envelope).ShouldNotBeNull().Diagnostics.ShouldStartWith($"No Condition in this folder is coded in {Loinc}; Conditions here use {Sct}.");
    }

    [Fact]
    public void GivenANotModifierOnAnUnusedSystem_WhenTheResultIsNotEmpty_ThenThereIsNoIssue()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<SearchFacts?> envelope = Search(folder, "Condition", $"code:not={Icd10}|I25");

        envelope.Facts!.Total.ShouldBe(2);
        Hint(envelope).ShouldBeNull();
    }

    [Fact]
    public void GivenAnUnusedSystemArgument_WhenCodesAreLookedUpByWordsAndNothingMatches_ThenTheIssueNamesTheSystem()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<CodeTextFacts?> envelope = FolderCodeSystems.SearchText(SearchFixture.Open(folder), "heart", resourceType: "Condition", system: Icd10, patient: null, count: null, offset: 0, CancellationToken.None);

        envelope.Facts!.Total.ShouldBe(0);
        envelope.Issues.Single(issue => issue.Code == Code).Diagnostics.ShouldBe($"No Condition in this folder is coded in {Icd10}; Conditions here use {Sct}, http://terminology.hl7.org/CodeSystem/condition-clinical. Search the folder's codes by words (folder_code_systems text=...) and use those.");
    }

    [Theory]
    [InlineData(Sct)]
    [InlineData("sct")]
    public void GivenAUsedSystemArgumentOrItsAlias_WhenNothingMatchesTheWords_ThenThereIsNoIssue(string system)
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<CodeTextFacts?> envelope = FolderCodeSystems.SearchText(SearchFixture.Open(folder), "zzz", resourceType: null, system: system, patient: null, count: null, offset: 0, CancellationToken.None);

        envelope.Facts!.Total.ShouldBe(0);
        envelope.Issues.ShouldNotContain(issue => issue.Code == Code);
    }

    [Fact]
    public void GivenAnUnusedSystemArgumentAndNoType_WhenNothingMatches_ThenTheSentenceReadsWithALowerCaseFolder()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<CodeTextFacts?> envelope = FolderCodeSystems.SearchText(SearchFixture.Open(folder), "heart", resourceType: null, system: Icd10, patient: null, count: null, offset: 0, CancellationToken.None);

        envelope.Issues.Single(issue => issue.Code == Code).Diagnostics.ShouldContain("; the folder's resources use ");
    }

    [Fact]
    public void GivenNoSystemArgument_WhenNothingMatchesTheWords_ThenThereIsNoIssue()
    {
        using TestFolder folder = SnomedConditions();

        ToolEnvelope<CodeTextFacts?> envelope = FolderCodeSystems.SearchText(SearchFixture.Open(folder), "I25", resourceType: null, system: null, patient: null, count: null, offset: 0, CancellationToken.None);

        envelope.Facts!.Total.ShouldBe(0);
        envelope.Issues.ShouldNotContain(issue => issue.Code == Code);
    }
}
