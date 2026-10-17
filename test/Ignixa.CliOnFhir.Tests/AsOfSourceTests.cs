// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Cohorts;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// summary / patient_summary and patient_cohort read their asOf through <see cref="AsOfClock"/> (the V14 merge's one option) and
/// say where it came from in <c>asOfSource</c>: <c>argument</c>, <c>session-option</c>, <c>patient-latest</c> (the map's default)
/// or <c>clock</c> (the cohort's default, V15's <c>default-now</c>). A date keeps V14's meaning, that day's first instant in UTC.
/// </summary>
public class AsOfSourceTests
{
    private static TestFolder Chart() => new TestFolder().Write("chart.ndjson", """
        {"resourceType":"Patient","id":"m1","identifier":[{"system":"urn:mrn","value":"MRN-1001"}],"birthDate":"1970-01-01","gender":"female"}
        {"resourceType":"Patient","id":"m2","birthDate":"1980-01-01","gender":"male"}
        {"resourceType":"Encounter","id":"e1","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/m1"},"period":{"start":"2015-01-01T10:00:00Z"}}
        {"resourceType":"Encounter","id":"e2","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/m1"},"period":{"start":"2016-06-01T10:00:00Z"}}
        """);

    private static MapRecord Record(ToolEnvelope<PatientMapFacts?> envelope) => envelope.Facts.ShouldNotBeNull().Record;

    [Fact]
    public void GivenNoAsOf_WhenSummarized_ThenTheMapIsReadAsOfThePatientsLatestDateAndSaysPatientLatest()
    {
        using TestFolder folder = Chart();
        FolderSession session = PatientFixture.Open(folder);

        MapRecord record = Record(PatientMap.Summarize(session, "m1", new AsOfClock(session, sessionOption: null)));

        record.AsOf.ShouldBeNull();
        record.AsOfSource.ShouldBe(AsOfSources.PatientLatest);
    }

    [Fact]
    public void GivenNoAsOfAndAPatientWithNoDatedResource_WhenSummarized_ThenTheClockIsTheSource()
    {
        using TestFolder folder = Chart();

        Record(PatientMap.Summarize(PatientFixture.Open(folder), "m2")).AsOfSource.ShouldBe(AsOfSources.Clock);
    }

    [Fact]
    public void GivenTheSessionOptionAsADate_WhenSummarized_ThenTheEncountersAreThoseUpToThatDaysFirstInstant()
    {
        using TestFolder folder = Chart();
        FolderSession session = PatientFixture.Open(folder);

        PatientMapFacts facts = PatientMap.Summarize(session, "MRN-1001", new AsOfClock(session, "2016-06-01")).Facts.ShouldNotBeNull();

        facts.Record.AsOf.ShouldBe("2016-06-01");
        facts.Record.AsOfSource.ShouldBe(AsOfSources.SessionOption);
        facts.Domains["encounters"].Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("2016-06-01", "2016-06-01", 1)]
    [InlineData("2016-06-01T05:00:00-05:00", "2016-06-01T10:00:00Z", 2)]
    public void GivenAnAsOfArgument_WhenSummarized_ThenItIsReadInUtcAndTheSourceIsArgument(string asOf, string echoed, int encounters)
    {
        using TestFolder folder = Chart();

        PatientMapFacts facts = PatientMap.Summarize(PatientFixture.Open(folder), "m1", asOf).Facts.ShouldNotBeNull();

        facts.Record.AsOf.ShouldBe(echoed);
        facts.Record.AsOfSource.ShouldBe(AsOfSources.Argument);
        facts.Domains["encounters"].Count.ShouldBe(encounters);
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2016-06")]
    [InlineData("2016-06-01T10:00:00")]
    public void GivenAnAsOfArgumentThatIsNeitherADateNorAnInstant_WhenSummarized_ThenTheCallFailsAsInvalidAsOf(string asOf)
    {
        using TestFolder folder = Chart();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(PatientFixture.Open(folder), "m1", asOf);

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe(AsOfClock.InvalidCode);
    }

    [Theory]
    [InlineData("2026-08-17", "2026-08-17")]
    [InlineData(" 2026-08-17 ", "2026-08-17")]
    [InlineData("2026-08-17T00:00:00-05:00", "2026-08-17T05:00:00Z")]
    public async Task GivenACohortAsOfArgument_WhenCounted_ThenItIsReadAsV14ReadItAndTheSourceIsArgument(string asOf, string echoed)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", CohortFixture.Patient("p1", "1970-01-01"));

        CohortFacts facts = CohortFixture.Facts(await PatientCohort.RunAsync(SearchFixture.Open(folder), new CohortArguments(asOf), CancellationToken.None));

        facts.AsOf.ShouldBe(echoed);
        facts.AsOfSource.ShouldBe(AsOfSources.Argument);
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026")]
    [InlineData("2026-08-17T10:00:00")]
    public async Task GivenACohortAsOfThatIsNeitherADateNorAnInstant_WhenCounted_ThenTheCallFailsAsInvalidAsOf(string asOf)
    {
        using TestFolder folder = new TestFolder().Write("patients.ndjson", CohortFixture.Patient("p1", "1970-01-01"));

        IssueItem issue = CohortFixture.Failure(await PatientCohort.RunAsync(SearchFixture.Open(folder), new CohortArguments(asOf), CancellationToken.None));

        issue.Code.ShouldBe(AsOfClock.InvalidCode);
        issue.Diagnostics.ShouldContain("not a FHIR date or instant");
    }
}
