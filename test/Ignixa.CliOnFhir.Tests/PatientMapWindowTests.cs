// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b, item 4: the patient map is read in a window that ends at asOf, which <c>record.window</c> states. A resource dated after
/// asOf is in no list, latest value or count, and a <c>dated-after-as-of</c> note counts them by type. Regression: at aaa08fda a
/// Condition, Procedure or Immunization dated after a stated asOf was listed and counted as if it had happened.
/// </summary>
public class PatientMapWindowTests
{
    private const string Sct = "http://snomed.info/sct";

    // Before 2020-01-01: c1, pr1, im1 and e1. After it: c2, pr2, im2 and e2.
    private static TestFolder Chart() => new TestFolder().Write("chart.ndjson", $$$"""
        {"resourceType":"Patient","id":"m1","birthDate":"1970-01-01","gender":"female"}
        {"resourceType":"Condition","id":"c1","subject":{"reference":"Patient/m1"},"clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"active"}]},"code":{"coding":[{"system":"{{{Sct}}}","code":"44054006","display":"Diabetes"}]},"onsetDateTime":"2015-01-01"}
        {"resourceType":"Condition","id":"c2","subject":{"reference":"Patient/m1"},"clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"active"}]},"code":{"coding":[{"system":"{{{Sct}}}","code":"38341003","display":"Hypertension"}]},"onsetDateTime":"2021-01-01"}
        {"resourceType":"Procedure","id":"pr1","status":"completed","subject":{"reference":"Patient/m1"},"code":{"coding":[{"system":"{{{Sct}}}","code":"80146002","display":"Appendectomy"}]},"performedDateTime":"2016-01-01"}
        {"resourceType":"Procedure","id":"pr2","status":"completed","subject":{"reference":"Patient/m1"},"code":{"coding":[{"system":"{{{Sct}}}","code":"73761001","display":"Colonoscopy"}]},"performedDateTime":"2022-01-01"}
        {"resourceType":"Immunization","id":"im1","status":"completed","patient":{"reference":"Patient/m1"},"vaccineCode":{"coding":[{"system":"http://hl7.org/fhir/sid/cvx","code":"140"}]},"occurrenceDateTime":"2017-01-01"}
        {"resourceType":"Immunization","id":"im2","status":"completed","patient":{"reference":"Patient/m1"},"vaccineCode":{"coding":[{"system":"http://hl7.org/fhir/sid/cvx","code":"208"}]},"occurrenceDateTime":"2023-01-01"}
        {"resourceType":"Encounter","id":"e1","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/m1"},"period":{"start":"2018-01-01T10:00:00Z"}}
        {"resourceType":"Encounter","id":"e2","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/m1"},"period":{"start":"2024-01-01T10:00:00Z"}}
        """);

    [Fact]
    public void GivenAnAsOfBeforePartOfTheRecord_WhenMapped_ThenWhatIsDatedAfterItIsInNoListOrCountAndANoteCountsIt()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(PatientFixture.Open(folder), "m1", "2020-01-01");

        PatientMapFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Domains["problems"].Count.ShouldBe(1);
        facts.Domains["procedures"].Count.ShouldBe(1);
        facts.Domains["immunizations"].Count.ShouldBe(1);
        facts.Domains["encounters"].Count.ShouldBe(1);
        string.Join('\n', facts.Domains.Values.SelectMany(domain => domain.Top ?? [])).ShouldNotContain("Condition/c2");
        IssueItem note = envelope.Issues.Single(issue => issue.Code == PatientContext.AfterAsOfCode);
        note.Severity.ShouldBe("information");
        note.Diagnostics.ShouldStartWith("4 resources of the record are dated after asOf 2020-01-01 (Condition 1, Encounter 1, Immunization 1, Procedure 1)");
    }

    [Fact]
    public void GivenAnAsOf_WhenMapped_ThenTheRecordStatesTheWindowEndingAtIt()
    {
        using TestFolder folder = Chart();

        MapRecord record = PatientMap.Summarize(PatientFixture.Open(folder), "m1", "2020-01-01").Facts.ShouldNotBeNull().Record;

        record.Window.ShouldBe(new ContextWindow(null, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void GivenNoAsOf_WhenMapped_ThenTheWindowEndsAtThePatientsLatestDateAndNothingIsLeftOut()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(PatientFixture.Open(folder), "m1");

        PatientMapFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Record.Window.To.ShouldBe(new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero));
        facts.Domains["problems"].Count.ShouldBe(2);
        facts.Domains["procedures"].Count.ShouldBe(2);
        envelope.Issues.ShouldNotContain(issue => issue.Code == PatientContext.AfterAsOfCode);
    }

    [Fact]
    public void GivenAnAsOf_WhenTheFullRootIsRead_ThenItsDomainsLeaveOutWhatIsDatedAfterItToo()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(PatientFixture.Open(folder), "m1", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        envelope.Facts.ShouldNotBeNull().Problems.Count.ShouldBe(1);
        envelope.Issues.ShouldContain(issue => issue.Code == PatientContext.AfterAsOfCode);
    }
}
