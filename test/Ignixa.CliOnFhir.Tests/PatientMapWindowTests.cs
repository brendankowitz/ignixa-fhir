// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b, item 4: the patient map is read in a window that ends at asOf, which <c>record.window</c> states. A resource dated after
/// asOf is in no list, latest value or count, and a <c>dated-after-as-of</c> note counts them by type. Regression: at aaa08fda a
/// Condition, Procedure or Immunization dated after a stated asOf was listed and counted as if it had happened. Task 12b fix round
/// 1 (I6, I7): at 86a9d902 record.resources, other, labs.excluded and find still counted or listed them, and the note left out
/// DiagnosticReport; the medication, allergy and administration exclusions had no test.
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

    // Before 2020-01-01: mr1, al1, ma1, o1, dr1 and dc1. After it: mr2, al2, ma2, o2, dr2 and dc2.
    private static TestFolder Wider() => new TestFolder().Write("chart.ndjson", $$$"""
        {"resourceType":"Patient","id":"w1","birthDate":"1970-01-01","gender":"female"}
        {"resourceType":"MedicationRequest","id":"mr1","status":"active","intent":"order","subject":{"reference":"Patient/w1"},"medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"860975","display":"Metformin"}]},"authoredOn":"2018-01-01"}
        {"resourceType":"MedicationRequest","id":"mr2","status":"active","intent":"order","subject":{"reference":"Patient/w1"},"medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"197361","display":"Amlodipine"}]},"authoredOn":"2021-01-01"}
        {"resourceType":"AllergyIntolerance","id":"al1","patient":{"reference":"Patient/w1"},"code":{"coding":[{"system":"{{{Sct}}}","code":"91935009","display":"Peanut"}]},"recordedDate":"2015-01-01"}
        {"resourceType":"AllergyIntolerance","id":"al2","patient":{"reference":"Patient/w1"},"code":{"coding":[{"system":"{{{Sct}}}","code":"300916003","display":"Latex"}]},"recordedDate":"2022-01-01"}
        {"resourceType":"MedicationAdministration","id":"ma1","status":"completed","subject":{"reference":"Patient/w1"},"medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"860975","display":"Metformin"}]},"effectiveDateTime":"2019-01-01"}
        {"resourceType":"MedicationAdministration","id":"ma2","status":"completed","subject":{"reference":"Patient/w1"},"medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"197361","display":"Amlodipine"}]},"effectiveDateTime":"2025-01-01"}
        {"resourceType":"Observation","id":"o1","status":"final","category":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/observation-category","code":"laboratory"}]}],"code":{"coding":[{"system":"http://loinc.org","code":"2339-0","display":"Glucose"}]},"subject":{"reference":"Patient/w1"},"effectiveDateTime":"2019-06-01","valueQuantity":{"value":100,"unit":"mg/dL"}}
        {"resourceType":"Observation","id":"o2","status":"final","category":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/observation-category","code":"laboratory"}]}],"code":{"coding":[{"system":"http://loinc.org","code":"2339-0","display":"Glucose"}]},"subject":{"reference":"Patient/w1"},"effectiveDateTime":"2021-06-01","valueQuantity":{"value":300,"unit":"mg/dL"}}
        {"resourceType":"DiagnosticReport","id":"dr1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"24323-8","display":"Metabolic panel"}]},"subject":{"reference":"Patient/w1"},"effectiveDateTime":"2019-06-01"}
        {"resourceType":"DiagnosticReport","id":"dr2","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"24323-8","display":"Metabolic panel"}]},"subject":{"reference":"Patient/w1"},"effectiveDateTime":"2021-06-01"}
        {"resourceType":"DocumentReference","id":"dc1","status":"current","subject":{"reference":"Patient/w1"},"date":"2019-02-01T00:00:00Z","content":[{"attachment":{"contentType":"text/plain"}}]}
        {"resourceType":"DocumentReference","id":"dc2","status":"current","subject":{"reference":"Patient/w1"},"date":"2022-02-01T00:00:00Z","content":[{"attachment":{"contentType":"text/plain"}}]}
        """);

    private static readonly DateTimeOffset Jan2020 = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GivenAnAsOf_WhenMapped_ThenMedicationsAllergiesAndAdministrationsDatedAfterItAreLeftOut()
    {
        using TestFolder folder = Wider();

        PatientMapFacts facts = PatientMap.Summarize(PatientFixture.Open(folder), "w1", "2020-01-01").Facts.ShouldNotBeNull();

        facts.Domains["medications"].Count.ShouldBe(1);
        facts.Domains["allergies"].Count.ShouldBe(1);
        facts.Domains["administrations"].Count.ShouldBe(1);
        string lines = string.Join('\n', facts.Domains.Values.SelectMany(domain => domain.Top ?? []));
        lines.ShouldContain("MedicationRequest/mr1");
        lines.ShouldContain("AllergyIntolerance/al1");
        lines.ShouldContain("MedicationAdministration/ma1");
        lines.ShouldNotContain("MedicationRequest/mr2");
        lines.ShouldNotContain("AllergyIntolerance/al2");
        lines.ShouldNotContain("MedicationAdministration/ma2");
    }

    [Fact]
    public void GivenAnAsOf_WhenTheFullRootIsRead_ThenMedicationsAllergiesAndAdministrationsDatedAfterItAreLeftOut()
    {
        using TestFolder folder = Wider();

        RootDigestFacts facts = ContextRoot.Get(PatientFixture.Open(folder), "w1", Jan2020).Facts.ShouldNotBeNull();

        facts.Medications.Count.ShouldBe(1);
        facts.Allergies.Count.ShouldBe(1);
        facts.Allergies.Items.ShouldHaveSingleItem().Reference.ShouldBe("AllergyIntolerance/al1");
    }

    [Fact]
    public void GivenAnAsOf_WhenMapped_ThenTheRecordOtherAndLabCountsLeaveOutWhatIsDatedAfterIt()
    {
        using TestFolder folder = Wider();

        PatientMapFacts facts = PatientMap.Summarize(PatientFixture.Open(folder), "w1", "2020-01-01").Facts.ShouldNotBeNull();

        // Of the 13 resources, the Patient and the six dated before 2020 are as of it.
        facts.Record.Resources.ShouldBe(7);
        facts.Other.ShouldBe(new Dictionary<string, int> { ["DiagnosticReport"] = 1, ["DocumentReference"] = 1 });
        facts.Domains["labs"].Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAnAsOf_WhenTheFullRootIsRead_ThenNoObservationDatedAfterItIsCountedAsExcludedFromLabs()
    {
        using TestFolder folder = Wider();

        DomainStub labs = ContextRoot.Get(PatientFixture.Open(folder), "w1", Jan2020).Facts.ShouldNotBeNull().Labs;

        labs.Count.ShouldBe(1);
        labs.Excluded.ShouldBe(0);
    }

    [Fact]
    public void GivenAnAsOf_WhenMapped_ThenTheNoteCountsEveryDatedTypeAfterItDiagnosticReportIncluded()
    {
        using TestFolder folder = Wider();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(PatientFixture.Open(folder), "w1", "2020-01-01");

        envelope.Issues.Single(issue => issue.Code == PatientContext.AfterAsOfCode).Diagnostics.ShouldStartWith(
            "6 resources of the record are dated after asOf 2020-01-01 (AllergyIntolerance 1, DiagnosticReport 1, DocumentReference 1, MedicationAdministration 1, MedicationRequest 1, Observation 1)");
    }

    [Fact]
    public void GivenAnAsOf_WhenFound_ThenAMatchDatedAfterItIsNotListed()
    {
        using TestFolder folder = Wider();
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: new ManualTimeProvider { Now = PatientFixture.AsOf }, asOfOption: "2020-01-01");

        ToolEnvelope<FindFacts?> envelope = ContextFinder.Find(session, "w1", "24323-8");

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(1);
        envelope.Facts.Items.ShouldHaveSingleItem().Reference.ShouldBe("DiagnosticReport/dr1");
    }

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
