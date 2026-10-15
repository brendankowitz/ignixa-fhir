// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The <c>compartment</c> argument of search and the folder's compartment enumeration. New capability: at 2e56e3f the tool had
/// no compartment argument and Ignixa's in-memory interpreter threw for every compartment search.
/// </summary>
public class CompartmentSearchTests
{
    /// <summary>Two patients; observations and encounters of each, one observation performed by p1 on p2, and an organization.</summary>
    private static TestFolder Ward() => new TestFolder()
        .Write("patients.ndjson", """
            {"resourceType":"Patient","id":"p1","gender":"female","managingOrganization":{"reference":"Organization/org1"}}
            {"resourceType":"Patient","id":"p2","gender":"male"}
            """)
        .Write("org.json", """{"resourceType":"Organization","id":"org1","name":"Acme"}""")
        .Write("obs.ndjson", """
            {"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"}}
            {"resourceType":"Observation","id":"o2","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8480-6"}]},"subject":{"reference":"Patient/p1"}}
            {"resourceType":"Observation","id":"o3","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p2"}}
            {"resourceType":"Observation","id":"o4","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p2"},"performer":[{"reference":"Patient/p1"}]}
            """)
        .Write("enc.ndjson", """
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/p1"}}
            {"resourceType":"Encounter","id":"e2","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/p2"}}
            """);

    [Theory]
    [InlineData("Observation", null, "o1,o2,o4")]
    [InlineData("Observation", "code=http://loinc.org|8867-4", "o1,o4")]
    [InlineData("Encounter", null, "e1")]
    [InlineData("Encounter", "_id=e2", "")]
    public void GivenAPatientCompartment_WhenSearched_ThenOnlyThatPatientsResourcesMatch(string resourceType, string? query, string expected)
    {
        // o3 and e2 reference the other patient; o4 is in p1's compartment through performer.
        using TestFolder folder = Ward();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest(resourceType, query, Compartment: "Patient/p1"));

        Ids(envelope).ShouldBe(expected.Length == 0 ? [] : expected.Split(','));
        envelope.Facts!.Compartment.ShouldBe("Patient/p1");
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenATypeNeverInAPatientCompartment_WhenSearchedInOne_ThenNothingMatchesAndAWarningSaysWhy()
    {
        // The organization is referenced by p1, but the R4 Patient CompartmentDefinition lists no parameter for Organization.
        using TestFolder folder = Ward();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Organization", Compartment: "Patient/p1"));

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(0);
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe("warning");
        issue.Code.ShouldBe("compartment-excludes-type");
        issue.Diagnostics.ShouldContain("Organization resources are never in a Patient compartment");
    }

    [Fact]
    public void GivenACompartmentWhoseResourceTheFolderLacks_WhenSearched_ThenReferencingResourcesStillMatchWithAnInformationIssue()
    {
        using TestFolder folder = new TestFolder().Write("obs.ndjson", """
            {"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/gone"}}
            """);

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Compartment: "Patient/gone"));

        Ids(envelope).ShouldBe(["o1"]);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("compartment-resource-absent");
    }

    [Theory]
    [InlineData("Patient", "'compartment' must be 'Type/id'")]
    [InlineData("Patient/p1/x", "'compartment' must be 'Type/id'")]
    [InlineData("Organization/org1", "use one of Device, Encounter, Patient, Practitioner, RelatedPerson")]
    [InlineData("patient/p1", "is not a R4 compartment type")]
    [InlineData("Patient/p_1", "is not a FHIR id")]
    public void GivenAMalformedCompartment_WhenSearched_ThenTheCallFailsAsAnInvalidParameter(string compartment, string expected)
    {
        using TestFolder folder = Ward();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Compartment: compartment));

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("invalid-parameter");
        issue.Diagnostics.ShouldContain(expected);
    }

    [Fact]
    public void GivenAnStu3TypeWhoseCompartmentCodesCannotBeEvaluated_WhenSearchedInTheCompartment_ThenTheCallFailsAsNotSupported()
    {
        using TestFolder folder = new TestFolder().Write("pat.json", """{"resourceType":"Patient","id":"p1"}""");
        var session = new FolderSession(folder.Path, new STU3CoreSchemaProvider(), NullLoggerFactory.Instance);

        IssueItem issue = ResourceSearcher.Search(session, new SearchRequest("AuditEvent", Compartment: "Patient/p1")).Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe("search-not-supported");
        issue.Diagnostics.ShouldContain("'compartment=Patient/p1' cannot be evaluated");
        issue.Diagnostics.ShouldContain("agent.patient, entity.patient");
    }

    [Fact]
    public void GivenATransactionBundle_WhenSearchedInAPatientCompartment_ThenTheResolvedUrnReferencesDecideMembership()
    {
        using TestFolder folder = new TestFolder().Write("bundle.json", """
            {"resourceType":"Bundle","type":"transaction","entry":[
              {"fullUrl":"urn:uuid:p-a","resource":{"resourceType":"Patient","id":"pa"},"request":{"method":"POST","url":"Patient"}},
              {"fullUrl":"urn:uuid:p-b","resource":{"resourceType":"Patient","id":"pb"},"request":{"method":"POST","url":"Patient"}},
              {"fullUrl":"urn:uuid:o-1","resource":{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:p-a"}},"request":{"method":"POST","url":"Observation"}},
              {"fullUrl":"urn:uuid:o-2","resource":{"resourceType":"Observation","id":"o2","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:p-b"}},"request":{"method":"POST","url":"Observation"}}
            ]}
            """);

        Ids(ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", Compartment: "Patient/pa"))).ShouldBe(["o1"]);
    }

    [Fact]
    public void GivenACompartmentAndAnInclude_WhenSearched_ThenTheIncludeFollowsTheCompartmentsMatches()
    {
        using TestFolder folder = Ward();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", "_include=Observation:subject&_sort=_id", Compartment: "Patient/p1"));

        Ids(envelope).ShouldBe(["o1", "o2", "o4"]);
        envelope.Facts!.Included.ShouldNotBeNull().Select(resource => (string)resource["id"]!).ShouldBe(["p1", "p2"]);
    }

    [Fact]
    public void GivenASearchWithAndWithoutACompartment_WhenSerialized_ThenFactsStateTheCompartmentOnlyWhenOneWasGiven()
    {
        using TestFolder folder = Ward();
        var tools = new ResourceTools(Open(folder));

        JsonNode scoped = Facts(tools.SearchFhirResources("Observation", summary: "count", compartment: "Patient/p1"));
        JsonNode whole = Facts(tools.SearchFhirResources("Observation", summary: "count"));

        ((string)scoped["compartment"]!).ShouldBe("Patient/p1");
        ((int)scoped["total"]!).ShouldBe(3);
        whole.AsObject().ContainsKey("compartment").ShouldBeFalse();
        ((int)whole["total"]!).ShouldBe(4);
    }

    [Fact]
    public void GivenAPatient_WhenItsCompartmentIsEnumerated_ThenEveryMemberIsReturnedTypeByTypeAndOnlyMemberTypesAreRead()
    {
        using TestFolder folder = Ward();
        var source = new FolderSearchSource(Open(folder));

        (IReadOnlyList<IndexedResource> members, IReadOnlyList<string> unevaluable) = source.CompartmentMembers("Patient", "p1");

        members.Select(member => member.Key.ToString()).ShouldBe(["Encounter/e1", "Observation/o1", "Observation/o2", "Observation/o4"]);
        unevaluable.ShouldBeEmpty();

        // Organization is held but never in a Patient compartment; Patient is a member type (through link) and is read.
        source.IndexesRead.Select(index => index.ResourceType).ShouldBe(["Encounter", "Observation", "Patient"]);
    }

    [Fact]
    public void GivenAnStu3Folder_WhenACompartmentIsEnumerated_ThenTypesThatCannotBeEvaluatedAreReportedAndNotRead()
    {
        using TestFolder folder = new TestFolder()
            .Write("audit.json", """{"resourceType":"AuditEvent","id":"a1","type":{"code":"rest"},"recorded":"2020-01-01T00:00:00Z","agent":[{"requestor":true}],"source":{"identifier":{"value":"x"}}}""")
            .Write("obs.json", """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/p1"}}""");
        var source = new FolderSearchSource(new FolderSession(folder.Path, new STU3CoreSchemaProvider(), NullLoggerFactory.Instance));

        (IReadOnlyList<IndexedResource> members, IReadOnlyList<string> unevaluable) = source.CompartmentMembers("Patient", "p1");

        members.Select(member => member.Key.ToString()).ShouldBe(["Observation/o1"]);
        unevaluable.ShouldBe(["AuditEvent"]);
        source.IndexesRead.Select(index => index.ResourceType).ShouldBe(["Observation"]);
    }

    private static JsonNode Facts(CallToolResult result) =>
        JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!["facts"]!;
}
