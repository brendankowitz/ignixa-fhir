// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Every tool that reads a type warns about that type's dropped duplicates (Task 5 review). Reviewer's repro: an id-less
/// Bundle entry whose RESTful fullUrl names Patient/123 shadows the real Patient/123 in a later file.
/// </summary>
public class DuplicateIdTests
{
    private const string Expected = "1 Patient resources repeat the type and id of an earlier one and were dropped, so every answer about Patient comes from the first occurrence only: Patient/123 used from a.json@";

    private static TestFolder Shadowed() => new TestFolder()
        .Write("a.json", """{"resourceType":"Bundle","type":"collection","entry":[{"fullUrl":"http://a.example.org/fhir/Patient/123","resource":{"resourceType":"Patient","gender":"male"}}]}""")
        .Write("z.ndjson", """{"resourceType":"Patient","id":"123","gender":"female"}""");

    // Regression: at 523e80d the search answered from the shadowing entry with no issue at all.
    [Fact]
    public void GivenAShadowedPatient_WhenSearched_ThenTheAnswerCarriesADuplicateIdWarningNamingBothLocations()
    {
        using TestFolder folder = Shadowed();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "gender=female"));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(0);
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe("warning");
        issue.Code.ShouldBe("duplicate-id");
        issue.Diagnostics.ShouldStartWith(Expected);
        issue.Diagnostics.ShouldEndWith(", ignored in z.ndjson:1.");
        issue.Location.ShouldBe("z.ndjson:1");
    }

    // Regression: at 523e80d a read of the shadowed id returned the other resource with no issue.
    [Fact]
    public void GivenAShadowedPatient_WhenRead_ThenTheAnswerCarriesTheWarning()
    {
        using TestFolder folder = Shadowed();

        ToolEnvelope<ReadFacts?> envelope = ResourceFetcher.Read(Open(folder), ["Patient/123"]);

        ((string)envelope.Facts.ShouldNotBeNull().Resources.ShouldHaveSingleItem()["gender"]!).ShouldBe("male");
        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldStartWith(Expected);
    }

    // Regression: at 523e80d a chain through the shadowed type was silent too; the warning follows every type a search reads.
    [Fact]
    public void GivenAShadowedPatient_WhenAChainReadsPatients_ThenTheWarningIsCarried()
    {
        using TestFolder folder = Shadowed().Write("enc.ndjson", """
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/123"}}
            """);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "subject:Patient.gender=female"));

        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("duplicate-id");
    }

    [Fact]
    public void GivenAShadowedPatient_WhenPatientToolsReadIt_ThenTheyCarryTheWarning()
    {
        using TestFolder folder = Shadowed();

        IssueItem fromList = PatientLister.List(Open(folder)).Issues.ShouldHaveSingleItem();
        IssueItem fromRoot = ContextRoot.Get(Open(folder), "123").Issues.Where(issue => issue.Code == "duplicate-id").ShouldHaveSingleItem();

        fromList.Diagnostics.ShouldStartWith(Expected);
        fromRoot.Diagnostics.ShouldStartWith(Expected);
    }

    // Guard: a type without duplicates carries no such warning even when another type has some.
    [Fact]
    public void GivenDuplicatesOfAnotherType_WhenSearched_ThenNoWarningIsCarried()
    {
        using TestFolder folder = Shadowed().Write("obs.ndjson", """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"}}""");

        ResourceSearcher.Search(Open(folder), new SearchRequest("Observation")).Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenMoreDuplicatesThanTheSampleLimit_WhenSearched_ThenAllAreCountedAndTheFirstFiveNamed()
    {
        string patients = string.Join('\n', Enumerable.Range(1, 7).Select(i => $$"""{"resourceType":"Patient","id":"p{{i}}"}"""));
        using TestFolder folder = new TestFolder().Write("a.ndjson", patients).Write("b.ndjson", patients);

        IssueItem issue = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient")).Issues.ShouldHaveSingleItem();

        issue.Diagnostics.ShouldStartWith("7 Patient resources repeat the type and id of an earlier one and were dropped, so every answer about Patient comes from the first occurrence only (first 5): Patient/p1 used from a.ndjson:1, ignored in b.ndjson:1; ");
        issue.Diagnostics.ShouldEndWith("Patient/p5 used from a.ndjson:5, ignored in b.ndjson:5.");
    }
}
