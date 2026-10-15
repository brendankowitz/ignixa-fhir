// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Chains, <c>_has</c>, <c>_sort</c> and includes through the tool's search, its envelope and its size budget.</summary>
public class SearchExtensionTests
{
    [Theory]
    [InlineData("subject:Patient.family=Smith", "o1,o2,o5")]
    [InlineData("subject.gender=male", "o3,o4")]
    [InlineData("subject:Patient.birthdate=lt1980&code=http://loinc.org|8867-4", "o1,o2,o5")]
    public void GivenForwardChain_WhenSearched_ThenTheReferencedPatientMustMatch(string query, string expected)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", query));

        Ids(envelope).ShouldBe(expected.Split(','));
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenChainWhoseTargetTypeIsAmbiguous_WhenSearched_ThenTheCallFailsNamingTheTypedForms()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "subject.name=Smith"));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("search-not-supported");
        issue.Diagnostics.ShouldContain("subject:Patient");
    }

    [Theory]
    [InlineData("_has:Observation:subject:code=http://loinc.org|8480-6", "p2")]
    [InlineData("_has:Observation:subject:code=http://loinc.org|8867-4", "p1,p2")]
    [InlineData("_has:Observation:subject:value-quantity=gt85", "p1,p2")]
    public void GivenReverseChain_WhenSearched_ThenAReferencingObservationMustMatch(string query, string expected)
    {
        using TestFolder folder = Clinic();

        Ids(ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", query))).ShouldBe(expected.Split(','));
    }

    [Theory]
    [InlineData("Observation", "_sort=-value-quantity", "o4,o5,o3,o2,o1")]
    [InlineData("Observation", "_sort=value-quantity", "o1,o2,o3,o5,o4")]
    [InlineData("Patient", "_sort=-birthdate", "p3,p2,p1")]
    [InlineData("Patient", "_sort=family,-birthdate", "p2,p3,p1")]
    public void GivenSort_WhenSearched_ThenResourcesComeBackInSortOrder(string type, string query, string expected)
    {
        using TestFolder folder = Clinic();

        Ids(ResourceSearcher.Search(Open(folder), new SearchRequest(type, query))).ShouldBe(expected.Split(','));
    }

    [Fact]
    public void GivenSortAndPaging_WhenTheNextPageIsRead_ThenItContinuesTheSortOrder()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> page = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_sort=birthdate", Count: 1, Offset: 1));

        Ids(page).ShouldBe(["p2"]);
        page.Omitted.ShouldHaveSingleItem().What.ShouldContain("offset=2");
    }

    [Theory]
    [InlineData("_sort=subject", "_sort=subject")]
    [InlineData("_sort=nope", "_sort=nope")]
    public void GivenSortByAParameterThatCannotBeOrdered_WhenSearched_ThenTheCallFailsInsteadOfReturningAnotherOrder(string query, string named)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", query));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("search-not-supported");
        issue.Diagnostics.ShouldContain(named);
    }

    [Fact]
    public void GivenInclude_WhenSearched_ThenReferencedPatientsAreReturnedSeparatelyWithEvidenceAndNotCounted()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "_include=Observation:subject&_count=2"));

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        Ids(envelope).ShouldBe(["o1", "o2"]);
        facts.Total.ShouldBe(5);
        facts.Returned.ShouldBe(2);
        ((string)facts.Included.ShouldNotBeNull().ShouldHaveSingleItem()["id"]!).ShouldBe("p1");
        envelope.Evidence.Select(item => item.Reference).ShouldBe(["Observation/o1", "Observation/o2", "Patient/p1"]);
        envelope.Evidence[2].File.ShouldBe(PatientsFile);
        envelope.Omitted.ShouldHaveSingleItem().What.ShouldContain("3 more matches");
    }

    [Fact]
    public void GivenRevInclude_WhenSearched_ThenObservationsOfTheReturnedPatientsAreIncluded()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_id=p2&_revinclude=Observation:subject"));

        envelope.Facts.ShouldNotBeNull().Included.ShouldNotBeNull().Select(resource => (string)resource["id"]!).ShouldBe(["o3", "o4"]);
    }

    [Fact]
    public void GivenIterateInclude_WhenSearched_ThenIncludedResourcesAreFollowedToo()
    {
        using TestFolder folder = new TestFolder()
            .Write("org.json", """{"resourceType":"Organization","id":"org1","name":"Acme"}""")
            .Write("pat.json", """{"resourceType":"Patient","id":"p1","managingOrganization":{"reference":"Organization/org1"}}""")
            .Write("enc.json", """{"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/p1"}}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Encounter", "_include=Encounter:subject&_include:iterate=Patient:organization"));

        envelope.Facts.ShouldNotBeNull().Included.ShouldNotBeNull().Select(resource => (string)resource["resourceType"]!).ShouldBe(["Patient", "Organization"]);
    }

    [Fact]
    public void GivenIncludeWithElements_WhenSearched_ThenOnlyTheMatchesAreTrimmed()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "_id=o1&_include=Observation:subject", Elements: "status"));

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Resources.ShouldNotBeNull().ShouldHaveSingleItem().AsObject().ContainsKey("code").ShouldBeFalse();
        facts.Included.ShouldNotBeNull().ShouldHaveSingleItem().AsObject().ContainsKey("name").ShouldBeTrue();
    }

    [Fact]
    public void GivenMoreIncludedResourcesThanTheCap_WhenSearched_ThenTheRestAreListedAsOmitted()
    {
        using TestFolder folder = new TestFolder()
            .Write("pat.json", """{"resourceType":"Patient","id":"p1"}""")
            .Write("obs.ndjson", string.Join('\n', Enumerable.Range(0, 60).Select(i =>
                "{\"resourceType\":\"Observation\",\"id\":\"o" + i + "\",\"status\":\"final\",\"code\":{\"text\":\"x\"},\"subject\":{\"reference\":\"Patient/p1\"}}")));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_revinclude=Observation:subject"));

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Included.ShouldNotBeNull().Count.ShouldBe(ResourceSearcher.IncludeLimit);
        OmittedItem omitted = envelope.Omitted.ShouldHaveSingleItem();
        omitted.What.ShouldContain("more included resources than the 50 returned");
        omitted.Why.ShouldBe("limit");
    }

    [Fact]
    public void GivenLargeIncludedResources_WhenSearched_ThenTheResponseStaysWithinTheCapAndSaysWhatWasLeftOut()
    {
        string notes = new('n', 3000);
        using TestFolder folder = new TestFolder()
            .Write("pat.json", """{"resourceType":"Patient","id":"p1"}""")
            .Write("obs.ndjson", string.Join('\n', Enumerable.Range(0, 40).Select(i =>
                "{\"resourceType\":\"Observation\",\"id\":\"o" + i + "\",\"status\":\"final\",\"code\":{\"text\":\"x\"},\"subject\":{\"reference\":\"Patient/p1\"},\"note\":[{\"text\":\"" + notes + "\"}]}")));

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_revinclude=Observation:subject"));

        EnvelopeJson.SizeOf(envelope).ShouldBeLessThanOrEqualTo(EnvelopeJson.MaxBytes);
        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Returned.ShouldBe(1);
        facts.Included.ShouldNotBeNull().Count.ShouldBeLessThan(40);
        envelope.Evidence.Count.ShouldBe(1 + facts.Included.Count);
        OmittedItem omitted = envelope.Omitted.ShouldHaveSingleItem();
        omitted.What.ShouldContain($"{40 - facts.Included.Count} more included resources");
        omitted.Why.ShouldBe("size-budget");
    }

    [Fact]
    public void GivenIncludeAndCountOnly_WhenSearched_ThenNoIncludesAreReturned()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "_include=Observation:subject", Summary: "count"));

        SearchFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Total.ShouldBe(5);
        facts.Included.ShouldBeNull();
    }

    [Fact]
    public void GivenChainOverPatientsThatCouldNotBeIndexed_WhenSearched_ThenThePatientDiagnosticsAreReported()
    {
        using TestFolder folder = new TestFolder()
            .Write("pat.ndjson", """
                {"resourceType":"Patient","id":"p1","gender":"male","meta":{"lastUpdated":"2020-01-01"}}
                """)
            .Write("obs.json", """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/p1"}}""");

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "subject:Patient.gender=male"));

        Ids(envelope).ShouldBe(["o1"]);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("index-extraction-failed");
    }

    [Fact]
    public void GivenIncludeOverMcp_WhenCalled_ThenTheResultCarriesIncludedResources()
    {
        using TestFolder folder = Clinic();
        var tools = new ResourceTools(Open(folder));

        CallToolResult result = tools.SearchFhirResources("Observation", "_include=Observation:subject", count: 1);

        (result.IsError ?? false).ShouldBeFalse();
        JsonNode envelope = JsonNode.Parse(result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text)!;
        envelope["facts"]!["included"]!.AsArray().Count.ShouldBe(1);
        ((string)envelope["facts"]!["included"]![0]!["id"]!).ShouldBe("p1");
    }

    [Fact]
    public void GivenLenientOverMcp_WhenAnUnknownParameterIsPassed_ThenItIsAWarningInsteadOfAnError()
    {
        using TestFolder folder = Clinic();
        var tools = new ResourceTools(Open(folder));

        CallToolResult strict = tools.SearchFhirResources("Patient", "familly=Smith");
        CallToolResult lenient = tools.SearchFhirResources("Patient", "familly=Smith", lenient: true);

        (strict.IsError ?? false).ShouldBeTrue();
        (lenient.IsError ?? false).ShouldBeFalse();
        JsonNode envelope = JsonNode.Parse(lenient.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text)!;
        ((string)envelope["issues"]![0]!["code"]!).ShouldBe("parameter-ignored");
    }

    [Theory]
    [InlineData("Patient", "general-practitioner:Practitioner.familly=x", "Did you mean 'family'?")]
    [InlineData("Observation", "subject:Patient.nmae=Smith", "Did you mean 'name'?")]
    [InlineData("Patient", "_has:Observation:subjekt:code=x", "Did you mean 'subject'?")]
    [InlineData("Patient", "_has:Observation:subject:cod=x", "Did you mean 'code'")]
    public void GivenATypoInsideAChain_WhenSearched_ThenTheCallFailsAsAnUnknownParameterOfTheLinksType(string type, string query, string hint)
    {
        using TestFolder folder = Clinic();

        IssueItem issue = ResourceSearcher.Search(Open(folder), new SearchRequest(type, query)).Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe("unknown-search-parameter");
        issue.Diagnostics.ShouldContain(hint);
        issue.Diagnostics.ShouldNotContain("..");
    }

    [Fact]
    public void GivenATypoInsideAChainInLenientMode_WhenSearched_ThenItIsIgnoredWithAWarning()
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "subject:Patient.nmae=Smith", Lenient: true));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(5);
        IssueItem warning = envelope.Issues.ShouldHaveSingleItem();
        warning.Code.ShouldBe("parameter-ignored");
        warning.Diagnostics.ShouldNotContain("Pass lenient");
    }

    [Theory]
    [InlineData("Observation", "_has:Observation:subject:code=x", "never references Observation")]
    [InlineData("Patient", "_has:Nope:subject:code=x", "'Nope' is not a")]
    [InlineData("Patient", "_has:Observation:code:code=x", "_has needs a reference parameter")]
    [InlineData("Observation", "subject:Practitioner.name=x", "never references Practitioner")]
    public void GivenAChainLinkThatCanNeverResolve_WhenSearched_ThenTheCallFailsAsAnInvalidRequest(string type, string query, string reason)
    {
        using TestFolder folder = Clinic();

        IssueItem issue = ResourceSearcher.Search(Open(folder), new SearchRequest(type, query)).Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe("invalid-search-request");
        issue.Diagnostics.ShouldContain(reason);
    }

    [Theory]
    [InlineData("Observation", "_include=Patient:organization", "_include=Patient:organization")]
    [InlineData("Patient", "_revinclude=Encounter:service-provider", "_revinclude=Encounter:service-provider")]
    public void GivenAnIncludeThatCanAddNothing_WhenSearched_ThenAWarningSaysSo(string type, string query, string named)
    {
        using TestFolder folder = Clinic();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest(type, query));

        envelope.Facts.ShouldNotBeNull().Included.ShouldNotBeNull().ShouldBeEmpty();
        IssueItem warning = envelope.Issues.ShouldHaveSingleItem();
        warning.Code.ShouldBe("include-not-applicable");
        warning.Diagnostics.ShouldContain(named);
    }

    [Fact]
    public void GivenAnIterateIncludeReachedThroughAnotherInclude_WhenSearched_ThenNoApplicabilityWarningIsRaised()
    {
        using TestFolder folder = Clinic();

        ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "_include=Observation:subject&_include:iterate=Patient:organization"))
            .Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenReferencesThatAreNotLocal_WhenChainedOrIncluded_ThenAnInformationIssueCountsWhatWasNotFollowed()
    {
        using TestFolder folder = new TestFolder()
            .Write("pat.json", """{"resourceType":"Patient","id":"p1","gender":"male"}""")
            .Write("enc.ndjson", """
                {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"urn:uuid:5fec133b-2286-84c5-2d10-5fe2ddbc3e76"}}
                {"resourceType":"Encounter","id":"e2","status":"finished","class":{"code":"AMB"},"subject":{"reference":"http://elsewhere.example.org/fhir/Patient/p1"}}
                {"resourceType":"Encounter","id":"e3","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/p1"}}
                """);
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> chained = ResourceSearcher.Search(session, new SearchRequest("Encounter", "subject:Patient.gender=male"));
        ToolEnvelope<SearchFacts?> included = ResourceSearcher.Search(session, new SearchRequest("Encounter", "_id=e1&_include=Encounter:subject"));

        Ids(chained).ShouldBe(["e3"]);
        IssueItem issue = chained.Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe("information");
        issue.Code.ShouldBe("references-unresolved");
        issue.Diagnostics.ShouldContain("Encounter:subject");
        issue.Diagnostics.ShouldContain("1 absolute or untyped references, and 1 Encounter resources holding urn: references");
        included.Issues.ShouldHaveSingleItem().Diagnostics.ShouldContain("0 absolute or untyped references, and 1 Encounter resources holding urn: references");
    }

    [Fact]
    public void GivenAnIdWithASystem_WhenSearched_ThenTheCallFailsAsAnInvalidRequestNamingTheParameter()
    {
        using TestFolder folder = Clinic();

        IssueItem issue = ResourceSearcher.Search(Open(folder), new SearchRequest("Patient", "_id=|p1")).Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe("invalid-search-request");
        issue.Diagnostics.ShouldContain("_id=|p1");
    }

    [Theory]
    [InlineData("characteristic-value=age$40", 0)]
    [InlineData("characteristic-value=gender$male", 1)]
    public void GivenCompositeComponentsOfDifferentKinds_WhenSearched_ThenAnOddResourceDoesNotFailTheSearch(string query, int total)
    {
        using TestFolder folder = new TestFolder().Write("groups.ndjson", """
            {"resourceType":"Group","id":"g1","type":"person","actual":true,"characteristic":[{"code":{"coding":[{"system":"http://s","code":"gender"}]},"valueCodeableConcept":{"coding":[{"code":"male"}]},"exclude":false}]}
            {"resourceType":"Group","id":"g2","type":"person","actual":true,"characteristic":[{"code":{"coding":[{"system":"http://s","code":"age"}]},"valueQuantity":{"value":40,"unit":"a","system":"http://unitsofmeasure.org","code":"a"},"exclude":false}]}
            {"resourceType":"Group","id":"g3","type":"person","actual":true,"characteristic":[{"code":{"coding":[{"system":"http://s","code":"flag"}]},"valueBoolean":true,"exclude":false}]}
            """);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Group", query));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(total);
        envelope.Issues.ShouldBeEmpty();
    }
}
