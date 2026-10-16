// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>patient_list</c> with a <c>query</c> and without the per-call date rule. New capability: at f8b47276 the tool had no
/// filter and every page carried <c>facts.dateRule</c>.
/// </summary>
public class PatientListQueryTests
{
    private static TestFolder Mrns() => new TestFolder().Write("patients.ndjson", """
        {"resourceType":"Patient","id":"m1","identifier":[{"system":"urn:mrn","value":"MRN-1001"}],"name":[{"use":"official","family":"Crist","given":["Madalyn472"]}],"birthDate":"1994-12-29","gender":"female"}
        {"resourceType":"Patient","id":"m2","identifier":[{"value":"MRN-2002"}],"name":[{"use":"official","family":"Abbott","given":["Bo"]}],"birthDate":"1980-01-01","gender":"male"}
        {"resourceType":"Patient","id":"m3","name":[{"use":"official","family":"Dunn","given":["Cy"]}],"birthDate":"1994-05-05","gender":"male"}
        {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/m1"},"period":{"start":"2020-01-01"}}
        """);

    [Theory]
    [InlineData("madalyn", "m1")]
    [InlineData("CRIST", "m1")]
    [InlineData("MRN-2002", "m2")]
    [InlineData("2002", "m2")]
    [InlineData("m3", "m3")]
    [InlineData("1994-12-29", "m1")]
    [InlineData("1994 dunn", "m3")]
    public void GivenAQuery_WhenListed_ThenOnlyPatientsMatchingEveryTermAreReturned(string query, string expectedId)
    {
        using TestFolder folder = Mrns();

        ToolEnvelope<PatientListFacts?> envelope = PatientLister.List(Open(folder), query: query);

        PatientListFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Patients.ShouldHaveSingleItem().Id.ShouldBe(expectedId);
        facts.Total.ShouldBe(1);
        facts.Query.ShouldBe(query);
        envelope.Evidence.ShouldHaveSingleItem().Reference.ShouldBe($"Patient/{expectedId}");
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAQueryMatchingSeveral_WhenListed_ThenTotalCountsTheMatchesAndNextKeepsTheQuery()
    {
        using TestFolder folder = Mrns();

        ToolEnvelope<PatientListFacts?> envelope = PatientLister.List(Open(folder), count: 1, query: "1994");

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(2);
        envelope.Facts.Patients.Select(patient => patient.Id).ShouldBe(["m3"]);
        envelope.Omitted.ShouldBe([new OmittedItem("1 more patients (offset=1)", "limit")]);
        envelope.Next[0].Args.ShouldBe(new Dictionary<string, object> { ["offset"] = 1, ["sort"] = "name", ["query"] = "1994" }, ignoreOrder: true);
    }

    [Fact]
    public void GivenAQueryMatchingNobody_WhenListed_ThenNoPatientsAreReturnedAndAbsentSaysWhatWasChecked()
    {
        using TestFolder folder = Mrns();

        ToolEnvelope<PatientListFacts?> envelope = PatientLister.List(Open(folder), query: "nobody");

        envelope.Facts.ShouldNotBeNull().Patients.ShouldBeEmpty();
        AbsentItem absent = envelope.Absent.ShouldHaveSingleItem();
        absent.What.ShouldBe("Patient matching 'nobody'");
        absent.Checked.ShouldContain("3 Patient resources");
        absent.Checked.ShouldContain("identifier values");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GivenABlankQuery_WhenListed_ThenTheCallFailsWithInvalidParameter(string query)
    {
        using TestFolder folder = Mrns();

        ToolEnvelope<PatientListFacts?> envelope = PatientLister.List(Open(folder), query: query);

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenNoQuery_WhenListed_ThenEveryPatientIsListedAndNoQueryIsEchoed()
    {
        using TestFolder folder = Mrns();

        PatientListFacts facts = PatientLister.List(Open(folder)).Facts.ShouldNotBeNull();

        facts.Total.ShouldBe(3);
        facts.Query.ShouldBeNull();
    }

    [Fact]
    public void GivenTheDefaultSort_WhenListedOverMcp_ThenNeitherTheDateRuleNorAQueryIsInTheFacts()
    {
        using TestFolder folder = Mrns();

        JsonNode facts = Facts(new PatientTools(Open(folder)).PatientList());

        facts.AsObject().Select(property => property.Key).ShouldBe(["total", "offset", "returned", "sort", "patients", "valueBindings"], ignoreOrder: true);
    }

    [Fact]
    public void GivenSortByLatest_WhenListed_ThenTheDateRuleIsGivenBecauseItDecidesTheOrder()
    {
        using TestFolder folder = Mrns();

        PatientListFacts facts = PatientLister.List(Open(folder), sort: "latest").Facts.ShouldNotBeNull();

        facts.DateRule.ShouldNotBeNull()["Encounter"].ShouldBe("period.start");
    }

    [Fact]
    public void GivenAnIdentifierQuery_WhenListedOverMcp_ThenTheWholeResponseIsSmall()
    {
        using TestFolder folder = Mrns();
        CallToolResult result = new PatientTools(Open(folder)).PatientList(query: "MRN-1001");

        string text = result.Content.OfType<TextContentBlock>().Single().Text;

        text.Length.ShouldBeLessThan(1500);
        text.ShouldNotContain("dateRule");
    }

    // New at V5: before it every row carried its compartment, so a lookup read the index of every type in a compartment.
    [Theory]
    [InlineData("Madalyn472")]
    [InlineData("MRN-1001")]
    [InlineData("m1")]
    public void GivenAQuery_WhenListed_ThenItFindsThePatientTheListingHasWithoutBuildingAnyIndex(string query)
    {
        using TestFolder folder = Mrns();
        FolderSession lookupSession = Open(folder);

        PatientSummary found = PatientLister.List(lookupSession, query: query).Facts.ShouldNotBeNull().Patients.ShouldHaveSingleItem();
        PatientSummary listed = PatientLister.List(Open(folder)).Facts.ShouldNotBeNull().Patients.Single(patient => patient.Id == found.Id);

        found.ShouldBe(new PatientSummary(listed.Id, listed.Name, listed.Gender, listed.BirthDate));
        lookupSession.BuiltIndexTypes.ShouldBeEmpty();
        listed.ResourceCount.ShouldBe(2);
    }

    [Fact]
    public void GivenALookup_WhenListedOverMcp_ThenRowsCarryOnlyThePatientsOwnFields()
    {
        using TestFolder folder = Mrns();

        JsonNode facts = Facts(new PatientTools(Open(folder)).PatientList(query: "MRN-1001"));

        facts["patients"]![0]!.AsObject().Select(property => property.Key).ShouldBe(["id", "name", "gender", "birthDate"]);
    }

    [Theory]
    [InlineData("urn:mrn|MRN-1001", "m1")]
    [InlineData("|MRN-2002", "m2")]
    [InlineData("|MRN-2002 abbott", "m2")]
    public void GivenAnIdentifierToken_WhenListed_ThenItMatchesThatIdentifierExactly(string query, string expectedId)
    {
        using TestFolder folder = Mrns();

        PatientLister.List(Open(folder), query: query).Facts.ShouldNotBeNull().Patients.ShouldHaveSingleItem().Id.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData("urn:other|MRN-1001")]
    [InlineData("urn:mrn|MRN-100")]
    [InlineData("|mrn-1001")]
    [InlineData("|MRN-1001")]
    public void GivenAnIdentifierTokenOfAnotherSystemOrValue_WhenListed_ThenNobodyMatches(string query)
    {
        using TestFolder folder = Mrns();

        PatientLister.List(Open(folder), query: query).Facts.ShouldNotBeNull().Patients.ShouldBeEmpty();
    }

    private static JsonNode Facts(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!["facts"]!;
}
