// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

public class ResourceSearchTests
{
    [Fact]
    public void GivenTokenQuery_WhenSearched_ThenOnlyResourcesWithThatSystemAndCodeMatch()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "code=http://loinc.org|8867-4"));

        Ids(envelope).ShouldBe(["o1", "o2", "o3", "o5"]);
        envelope.Facts!.Total.ShouldBe(4);
        envelope.Issues.ShouldBeEmpty();
        envelope.Omitted.ShouldBeEmpty();
    }

    [Fact]
    public void GivenStringQuery_WhenSearched_ThenPrefixMatchesAreCaseInsensitive()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "family=smi"));

        Ids(envelope).ShouldBe(["p1", "p3"]);
    }

    [Fact]
    public void GivenDateQuery_WhenSearched_ThenOnlyLaterBirthdatesMatch()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "birthdate=gt1980"));

        Ids(envelope).ShouldBe(["p2", "p3"]);
    }

    [Fact]
    public void GivenReferenceQuery_WhenSearched_ThenResourcesPointingAtThatTargetMatch()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "subject=Patient/p1"));

        Ids(envelope).ShouldBe(["o1", "o2", "o5"]);
    }

    [Fact]
    public void GivenReferenceByIdAlone_WhenSearched_ThenTheTargetTypeIsNotRequired()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "subject=p2"));

        Ids(envelope).ShouldBe(["o3", "o4"]);
    }

    [Fact]
    public void GivenReferenceAndTokenAndOrValues_WhenSearched_ThenParametersAreAndedAndValuesOred()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(
            Open(folder),
            new SearchRequest("Observation", "subject=Patient/p1,Patient/p2&code=http://loinc.org|8480-6,http://loinc.org|8867-4&value-quantity=gt70"));

        Ids(envelope).ShouldBe(["o2", "o3", "o4", "o5"]);
    }

    [Fact]
    public void GivenNegatedToken_WhenSearched_ThenNonMatchingResourcesAreReturned()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "code:not=http://loinc.org|8867-4"));

        Ids(envelope).ShouldBe(["o4"]);
    }

    [Fact]
    public void GivenIdQuery_WhenSearched_ThenResourcesAreMatchedByIdInCatalogOrder()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_id=p3,p1,nobody"));

        Ids(envelope).ShouldBe(["p1", "p3"]);
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenIdAndOtherParameter_WhenSearched_ThenBothMustHold()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_id=p1,p2&family=Smith"));

        Ids(envelope).ShouldBe(["p1"]);
    }

    [Fact]
    public void GivenNoQuery_WhenSearched_ThenEveryResourceOfTheTypeMatches()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient"));

        Ids(envelope).ShouldBe(["p1", "p2", "p3"]);
    }

    [Fact]
    public void GivenTypeWithNoResources_WhenSearched_ThenTheResultIsEmptyWithoutError()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Condition", "code=x"));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(0);
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenUnknownParameter_WhenSearched_ThenAWarningNamesItAndOtherFiltersStillApply()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "bogusparam=1&family=Jones"));

        Ids(envelope).ShouldBe(["p2"]);
        IssueItem warning = envelope.Issues.ShouldHaveSingleItem();
        warning.Severity.ShouldBe("warning");
        warning.Code.ShouldBe("parameter-ignored");
        warning.Diagnostics.ShouldContain("bogusparam=1");
    }

    [Fact]
    public void GivenUnrecognizedControlParameter_WhenSearched_ThenAWarningNamesIt()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_foo=1"));

        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldContain("_foo=1");
        envelope.Issues[0].Code.ShouldBe("parameter-ignored");
    }

    [Fact]
    public void GivenParameterWithoutValue_WhenSearched_ThenAWarningSaysItWasDropped()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "family=&gender=male"));

        Ids(envelope).ShouldBe(["p2", "p3"]);
        IssueItem warning = envelope.Issues.ShouldHaveSingleItem();
        warning.Code.ShouldBe("parameter-ignored");
        warning.Diagnostics.ShouldContain("family=");
    }

    [Fact]
    public void GivenSummaryOtherThanCount_WhenSearched_ThenAWarningSaysItWasIgnored()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_summary=true"));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBe(3);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("parameter-ignored");
    }

    [Theory]
    [InlineData("_sort=family", "_sort")]
    [InlineData("_include=Observation:subject", "_include")]
    [InlineData("_revinclude=Observation:subject", "_revinclude")]
    [InlineData("_lastUpdated=gt2020", "_lastUpdated")]
    [InlineData("_id:not=p1", "_id:not")]
    public void GivenUnsupportedFeature_WhenSearched_ThenTheCallFailsNamingTheFeature(string query, string feature)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", query));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe("error");
        issue.Code.ShouldBe("search-not-supported");
        issue.Diagnostics.ShouldContain(feature);
    }

    [Fact]
    public void GivenChainedParameter_WhenSearched_ThenTheCallFailsAsNotSupported()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "subject:Patient.family=Smith"));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("search-not-supported");
    }

    [Fact]
    public void GivenUnsupportedModifier_WhenSearched_ThenTheCallFailsInsteadOfWideningTheSearch()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "family:bogus=Smith"));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("search-not-supported");
        issue.Diagnostics.ShouldContain("family:bogus");
    }

    [Fact]
    public void GivenMalformedValue_WhenSearched_ThenTheCallFailsWithAnInvalidRequestIssue()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "birthdate=notadate"));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-search-request");
    }

    [Fact]
    public void GivenUnknownResourceType_WhenSearched_ThenTheCallFailsWithoutBuildingAnIndex()
    {
        using TestFolder folder = Clinic();
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(session, new SearchRequest("Pateint", "family=Smith"));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("unknown-resource-type");
        session.BuiltIndexTypes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(null, -3)]
    public void GivenOutOfRangePaging_WhenSearched_ThenTheCallFails(int? count, int offset)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", Count: count, Offset: offset));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenCountBelowOneInTheQuery_WhenSearched_ThenTheCallFailsInsteadOfReturningOne()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_count=0"));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenMoreMatchesThanTheDefaultCount_WhenSearched_ThenOmittedGivesTheNextOffset()
    {
        using TestFolder folder = Observations(count: 15);
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> first = ResourceSearcher.Search(session, new SearchRequest("Observation"));
        ToolEnvelope<SearchFacts?> second = ResourceSearcher.Search(session, new SearchRequest("Observation", Offset: 10));

        SearchFacts firstFacts = first.Facts.ShouldNotBeNull();
        firstFacts.Total.ShouldBe(15);
        firstFacts.Returned.ShouldBe(10);
        Ids(first).ShouldBe(Enumerable.Range(1, 10).Select(i => $"o{i:00}"));
        first.Omitted.ShouldHaveSingleItem().ShouldBe(new OmittedItem("5 more matches (offset=10)", "limit"));

        Ids(second).ShouldBe(Enumerable.Range(11, 5).Select(i => $"o{i:00}"));
        second.Facts.ShouldNotBeNull().Offset.ShouldBe(10);
        second.Omitted.ShouldBeEmpty();
    }

    [Fact]
    public void GivenCountArgumentAndQueryCount_WhenSearched_ThenTheArgumentWins()
    {
        using TestFolder folder = Observations(count: 15);
        FolderSession session = Open(folder);

        Ids(ResourceSearcher.Search(session, new SearchRequest("Observation", "_count=3"))).Length.ShouldBe(3);
        Ids(ResourceSearcher.Search(session, new SearchRequest("Observation", "_count=3", Count: 4))).Length.ShouldBe(4);
    }

    [Fact]
    public void GivenCountAboveTheMaximum_WhenSearched_ThenItIsClampedWithAWarning()
    {
        using TestFolder folder = Observations(count: 60);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Count: 500));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBe(ResourceSearcher.MaxCount);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("count-clamped");
        envelope.Omitted.ShouldHaveSingleItem().What.ShouldBe("10 more matches (offset=50)");
    }

    [Fact]
    public void GivenOffsetBeyondTheTotal_WhenSearched_ThenNoResourcesAreReturnedAndTheIssueSaysWhy()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", Offset: 5));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(3);
        envelope.Facts.Returned.ShouldBe(0);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("offset-beyond-total");
    }

    [Fact]
    public void GivenSummaryCount_WhenSearched_ThenOnlyTheTotalIsReturned()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "family=Smith", Summary: "count"));

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Total.ShouldBe(2);
        facts.Resources.ShouldBeNull();
        envelope.Evidence.ShouldBeEmpty();
        envelope.Omitted.ShouldBeEmpty();
    }

    [Fact]
    public void GivenSummaryCountInTheQuery_WhenSearched_ThenOnlyTheTotalIsReturned()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_summary=count"));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(3);
        envelope.Facts.Resources.ShouldBeNull();
    }

    [Fact]
    public void GivenSummaryWithAnotherValue_WhenSearched_ThenTheCallFails()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", Summary: "text"));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenElementsWithSummaryCount_WhenSearched_ThenAWarningSaysElementsWasIgnored()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", Summary: "count", Elements: "gender"));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(3);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("parameter-ignored");
    }

    [Fact]
    public void GivenElements_WhenSearched_ThenResourcesAreTrimmedAndTaggedSubsetted()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_id=p1", Elements: "gender,birthDate"));

        System.Text.Json.Nodes.JsonObject patient = Single(envelope).AsObject();
        patient.Select(property => property.Key).ShouldBe(["resourceType", "id", "birthDate", "gender", "meta"], ignoreOrder: true);
        System.Text.Json.Nodes.JsonObject tag = patient["meta"]!["tag"]!.AsArray().ShouldHaveSingleItem()!.AsObject();
        ((string)tag["system"]!).ShouldBe("http://terminology.hl7.org/CodeSystem/v3-ObservationValue");
        ((string)tag["code"]!).ShouldBe("SUBSETTED");
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenExistingMetaAndElementsInTheQuery_WhenSearched_ThenMetaIsKeptAndTheTagAdded()
    {
        using TestFolder folder = new TestFolder().Write(
            "p.json",
            """{"resourceType":"Patient","id":"p1","meta":{"versionId":"3","tag":[{"system":"http://example.org","code":"x"}]},"gender":"male","active":true}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_elements=gender"));

        System.Text.Json.Nodes.JsonObject meta = Single(envelope)["meta"]!.AsObject();
        ((string)meta["versionId"]!).ShouldBe("3");
        meta["tag"]!.AsArray().Select(tag => (string)tag!["code"]!).ShouldBe(["x", "SUBSETTED"]);
        Single(envelope).AsObject().ContainsKey("active").ShouldBeFalse();
    }

    [Fact]
    public void GivenChoiceElementNamedWithoutItsType_WhenSearched_ThenTheTypedPropertyIsKept()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "_id=o1", Elements: "value[x]"));

        Single(envelope).AsObject().ContainsKey("valueQuantity").ShouldBeTrue();
        Single(envelope).AsObject().ContainsKey("status").ShouldBeFalse();
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNoElements_WhenSearched_ThenResourcesAreReturnedWholeWithoutTheSubsettedTag()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_id=p1"));

        Single(envelope).AsObject().ContainsKey("name").ShouldBeTrue();
        Single(envelope).AsObject().ContainsKey("meta").ShouldBeFalse();
    }

    [Fact]
    public void GivenUnknownElement_WhenSearched_ThenAWarningNamesIt()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", Elements: "gender,nonsense"));

        IssueItem warning = envelope.Issues.ShouldHaveSingleItem();
        warning.Code.ShouldBe("element-ignored");
        warning.Diagnostics.ShouldContain("nonsense");
    }

    [Fact]
    public void GivenEmptyElements_WhenSearched_ThenTheCallFails()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", Elements: " , "));

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenResourcesLargerThanTheBudget_WhenSearched_ThenFewerAreReturnedAndOmittedSaysSo()
    {
        using TestFolder folder = Patients(count: 5, paddingBytes: 20_000);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient"));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(5);
        Ids(envelope).ShouldBe(["p0", "p1"]);
        envelope.Omitted.ShouldHaveSingleItem().ShouldBe(new OmittedItem("3 more matches (offset=2)", "size-budget"));
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("size-budget");
        envelope.Evidence.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenOneResourceLargerThanTheBudget_WhenSearched_ThenNoneIsReturnedAndTrimmingIsSuggested()
    {
        using TestFolder folder = Patients(count: 1, paddingBytes: 60_000);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient"));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBe(0);
        envelope.Omitted.ShouldHaveSingleItem().Why.ShouldBe("size-budget");
        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldContain("elements");
    }

    [Fact]
    public void GivenElementsThatBringResourcesUnderTheBudget_WhenSearched_ThenAllAreReturned()
    {
        using TestFolder folder = Patients(count: 5, paddingBytes: 20_000);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", Elements: "gender"));

        Ids(envelope).Length.ShouldBe(5);
        envelope.Omitted.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNdjsonAndSingleFileResources_WhenSearched_ThenEvidenceCarriesFileLineAndOffset()
    {
        using TestFolder folder = Clinic().Write("loose/q.json", """{"resourceType":"Patient","id":"q1","name":[{"family":"Quinn"}]}""");
        string patients = File.ReadAllText(Path.Combine(folder.Path, PatientsFile));
        long secondLineOffset = System.Text.Encoding.UTF8.GetByteCount(patients[..(patients.IndexOf('\n', StringComparison.Ordinal) + 1)]);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "family=Jones,Quinn"));

        envelope.Evidence.ShouldBe(
        [
            new EvidenceItem("Patient/q1", "loose/q.json", Line: null, Offset: 0),
            new EvidenceItem("Patient/p2", PatientsFile, Line: 2, secondLineOffset),
        ]);
    }

    [Fact]
    public void GivenFileChangedAfterIndexing_WhenSearched_ThenTheMovedResourceIsReportedNotReturned()
    {
        using TestFolder folder = Clinic();
        FolderSession session = Open(folder);
        session.GetIndex("Patient");
        // Same length, so every other resource keeps its recorded range and only p1's range now holds a different resource.
        folder.Write(PatientsFile, File.ReadAllText(Path.Combine(folder.Path, PatientsFile)).Replace("\"id\":\"p1\"", "\"id\":\"zz\"", StringComparison.Ordinal));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(session, new SearchRequest("Patient", "family=Smith"));

        Ids(envelope).ShouldBe(["p3"]);
        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(2);
        IssueItem problem = envelope.Issues.ShouldHaveSingleItem();
        problem.Code.ShouldBe("catalog-stale");
        problem.Location.ShouldBe($"{PatientsFile}:1");
        envelope.Freshness.StaleFiles.ShouldBe(1);
    }

    [Fact]
    public void GivenIndexDiagnostics_WhenSearched_ThenTheyAreReportedBecauseResultsMayBeIncomplete()
    {
        using TestFolder folder = new TestFolder().Write(
            "o.json",
            """{"resourceType":"Observation","id":"bad","status":"final","code":{"text":"x"},"effectiveDateTime":"garbage"}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation"));

        envelope.Facts.ShouldNotBeNull().Returned.ShouldBe(1);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("index-extraction-failed");
    }

    [Fact]
    public void GivenSearch_WhenAnswered_ThenFreshnessReportsTheIndexTimeAndNoStaleFiles()
    {
        using TestFolder folder = Clinic();
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(session, new SearchRequest("Patient"));

        envelope.Freshness.StaleFiles.ShouldBe(0);
        envelope.Freshness.IndexBuiltAt.ShouldBe(session.GetIndex("Patient").BuiltAt);
        envelope.Derived.ShouldBeNull();
    }

    private static TestFolder Observations(int count) => new TestFolder().Write(
        "obs.ndjson",
        string.Join('\n', Enumerable.Range(1, count).Select(i => $$$"""{"resourceType":"Observation","id":"o{{{i:00}}}","status":"final","code":{"text":"x"}}""")));

    private static TestFolder Patients(int count, int paddingBytes) => new TestFolder().Write(
        "patients.ndjson",
        string.Join('\n', Enumerable.Range(0, count).Select(i => $$$"""{"resourceType":"Patient","id":"p{{{i}}}","gender":"male","text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">{{{new string('x', paddingBytes)}}}</div>"}}""")));
}
