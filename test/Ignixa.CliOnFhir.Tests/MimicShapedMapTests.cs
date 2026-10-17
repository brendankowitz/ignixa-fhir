// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.PatientFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The patient map over data shaped like the MIMIC-IV Demo on FHIR: local code systems, dates shifted past the session clock
/// (the 2150s here, the 2100s in MIMIC), Conditions without clinicalStatus, observations under local categories, transfers as
/// Encounter.location entries and eMAR MedicationAdministrations. Regressions: at V15 the map read everything as of the clock,
/// so encounters and labs were empty, problems listed nothing and administrations were only an "other" count.
/// </summary>
public class MimicShapedMapTests
{
    private const string Mimic = "http://mimic.mit.edu/fhir/mimic";
    private const string ObsCategory = "http://terminology.hl7.org/CodeSystem/observation-category";

    [Fact]
    public void GivenDatesAfterTheClock_WhenMapped_ThenTheRecordIsReadAsOfItsOwnLatestDate()
    {
        using TestFolder folder = Chart();

        PatientMapFacts facts = PatientMap.Summarize(Open(folder), "10000001").Facts.ShouldNotBeNull();

        facts.Record.From.ShouldBe("2150-01-01");
        facts.Record.To.ShouldBe("2150-01-04");
        facts.Record.AsOf.ShouldBeNull();
        MapDomain encounters = facts.Domains["encounters"];
        encounters.Count.ShouldBe(2);
        encounters.ByClass.ShouldBe(new Dictionary<string, int> { ["ACUTE"] = 1, ["IMP"] = 1 });
        encounters.Transfers.ShouldBe(2);
        facts.Domains["labs"].Observations.ShouldBe(6);
    }

    [Fact]
    public void GivenLocalCategoriesAndCodes_WhenMapped_ThenTheyCountAsLabsGroupedByTheirOwnCodingAndCategory()
    {
        using TestFolder folder = Chart();

        MapDomain labs = PatientMap.Get(Open(folder), "m1").Facts.ShouldNotBeNull().Domains["labs"];

        // The ED exam is categorized by HL7 as exam, so it is the one Observation left out.
        labs.Count.ShouldBe(3);
        labs.Excluded.ShouldBe(1);
        labs.ByCategory.ShouldBe(new Dictionary<string, int> { ["Routine Vital Signs"] = 3, ["Restraint/Support Systems"] = 2, ["laboratory"] = 1 });

        // The string-valued chart item has as many observations as heart rate but no number, so it is named in more instead.
        labs.Top.ShouldBe([
            "mimic-chartevents-d-items|220045 Heart Rate @2150-01-03 = 90 bpm n=3 up Observation/hr3",
            "mimic-d-labitems|50912 Creatinine @2150-01-02 = 1.1 mg/dL Observation/cr1"]);
        labs.More.ShouldBe(["mimic-chartevents-d-items|227969 Safety Measures"]);
    }

    [Fact]
    public void GivenConditionsWithoutClinicalStatus_WhenMapped_ThenTheyAreUnknownNotInactiveAndTheMostFrequentAreListed()
    {
        using TestFolder folder = Chart();

        MapDomain problems = PatientMap.Get(Open(folder), "m1").Facts.ShouldNotBeNull().Domains["problems"];

        problems.Count.ShouldBe(3);
        problems.Active.ShouldBe(0);
        problems.NoStatus.ShouldBe(3);
        problems.Top.ShouldNotBeNull()[0].ShouldBe("mimic-diagnosis-icd9|4280 Congestive heart failure, unspecified n=2 no status Condition/c1");
        problems.Top.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenCompletedOrdersAndEmarAdministrations_WhenMapped_ThenNoneIsActiveAndTheAdministrationsAreADomain()
    {
        using TestFolder folder = Chart();

        PatientMapFacts facts = PatientMap.Get(Open(folder), "m1").Facts.ShouldNotBeNull();

        facts.Domains["medications"].ShouldBe(new MapDomain(1, Active: 0, Last: "2150-01-02", Top: facts.Domains["medications"].Top));
        facts.Domains["administrations"].Count.ShouldBe(2);
        facts.Domains["administrations"].Top.ShouldBe(["mimic-medication-name|Furosemide @2150-01-04 n=2 MedicationAdministration/ma2"]);
        facts.Domains["procedures"].Top.ShouldBe(["mimic-procedure-icd9|3893 Venous catheterization @2150-01-02 Procedure/x1"]);
        (facts.Other ?? new Dictionary<string, int>()).ShouldNotContainKey("MedicationAdministration");
    }

    [Fact]
    public void GivenSystemsUnderOneUrlPrefix_WhenMapped_ThenEachIsAliasedByItsLastSegmentAndThePrefixIsSpelledOnce()
    {
        using TestFolder folder = Chart();

        IReadOnlyList<string> systems = PatientMap.Get(Open(folder), "m1").Facts.ShouldNotBeNull().CodeSystems;

        systems.ShouldHaveSingleItem().ShouldStartWith($"{Mimic}/CodeSystem/{{alias}}: mimic-chartevents-d-items, ");
        systems[0].ShouldContain("mimic-diagnosis-icd9");
    }

    [Fact]
    public void GivenAnExplicitAsOfBeforeTheData_WhenSummarized_ThenTheTimeDependentDomainsAreEmptyAndTheAsOfIsStated()
    {
        using TestFolder folder = Chart();

        PatientMapFacts facts = PatientMap.Summarize(Open(folder), "m1", "2150-01-02").Facts.ShouldNotBeNull();

        facts.Record.AsOf.ShouldBe("2150-01-02");
        facts.Domains["encounters"].Count.ShouldBe(1);
        facts.Domains["labs"].Observations.ShouldBe(2);
    }

    [Fact]
    public void GivenAnAsOfThatIsNoDate_WhenSummarized_ThenTheCallFailsWithInvalidAsOf()
    {
        using TestFolder folder = Chart();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(Open(folder), "m1", "yesterday");

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-as-of");
    }

    [Fact]
    public void GivenAMimicShapedRecord_WhenMapped_ThenTheResponseStaysWithinTheMapBudget()
    {
        using TestFolder folder = Chart();

        int bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(PatientMap.Get(Open(folder), "m1"), EnvelopeJson.Compact));

        bytes.ShouldBeLessThan(4608);
    }

    private static TestFolder Chart() => new TestFolder().Write("mimic.ndjson", string.Join('\n',
        $$$"""{"resourceType":"Patient","id":"m1","identifier":[{"system":"{{{Mimic}}}/identifier/patient","value":"10000001"}],"name":[{"use":"official","family":"Patient_10000001"}],"gender":"male","birthDate":"2080-01-01"}""",
        Condition("c1", "4280", "Congestive heart failure, unspecified", "e1"),
        Condition("c2", "4280", "Congestive heart failure, unspecified", "e2"),
        Condition("c3", "42731", "Atrial fibrillation", "e2"),
        Encounter("e1", "IMP", "2150-01-01T08:00:00-05:00", locations: 3),
        Encounter("e2", "ACUTE", "2150-01-03T10:00:00-05:00", locations: 1),
        ChartEvent("hr1", "220045", "Heart Rate", "Routine Vital Signs", "2150-01-01T09:00:00-05:00", "\"valueQuantity\":{\"value\":70,\"unit\":\"bpm\"}"),
        ChartEvent("hr2", "220045", "Heart Rate", "Routine Vital Signs", "2150-01-02T09:00:00-05:00", "\"valueQuantity\":{\"value\":80,\"unit\":\"bpm\"}"),
        ChartEvent("hr3", "220045", "Heart Rate", "Routine Vital Signs", "2150-01-03T09:00:00-05:00", "\"valueQuantity\":{\"value\":90,\"unit\":\"bpm\"}"),
        ChartEvent("sm1", "227969", "Safety Measures", "Restraint/Support Systems", "2150-01-01T10:00:00-05:00", "\"valueString\":\"Bed in low position\""),
        ChartEvent("sm2", "227969", "Safety Measures", "Restraint/Support Systems", "2150-01-03T10:00:00-05:00", "\"valueString\":\"Side rails\""),
        $$$"""{"resourceType":"Observation","id":"cr1","status":"final","category":[{"coding":[{"system":"{{{ObsCategory}}}","code":"laboratory"}]}],"code":{"coding":[{"system":"{{{Mimic}}}/CodeSystem/mimic-d-labitems","code":"50912","display":"Creatinine"}]},"subject":{"reference":"Patient/m1"},"effectiveDateTime":"2150-01-02T06:00:00-05:00","valueQuantity":{"value":1.1,"unit":"mg/dL"}}""",
        $$$"""{"resourceType":"Observation","id":"ex1","status":"final","category":[{"coding":[{"system":"{{{ObsCategory}}}","code":"exam"}]}],"code":{"coding":[{"system":"http://loinc.org","code":"98137-3","display":"Pain assessment report"}]},"subject":{"reference":"Patient/m1"},"effectiveDateTime":"2150-01-01T08:30:00-05:00","valueString":"0"}""",
        $$$"""{"resourceType":"MedicationRequest","id":"mr1","status":"completed","intent":"order","subject":{"reference":"Patient/m1"},"medicationCodeableConcept":{"coding":[{"system":"{{{Mimic}}}/CodeSystem/mimic-medication-name","code":"Furosemide"}]},"authoredOn":"2150-01-02T07:00:00-05:00"}""",
        Administration("ma1", "2150-01-02T08:00:00-05:00"),
        Administration("ma2", "2150-01-04T08:00:00-05:00"),
        $$$"""{"resourceType":"Procedure","id":"x1","status":"completed","code":{"coding":[{"system":"{{{Mimic}}}/CodeSystem/mimic-procedure-icd9","code":"3893","display":"Venous catheterization"}]},"subject":{"reference":"Patient/m1"},"performedDateTime":"2150-01-02T00:00:00-05:00"}""",
        $$$"""{"resourceType":"Specimen","id":"s1","subject":{"reference":"Patient/m1"},"collection":{"collectedDateTime":"2150-01-05T00:00:00-05:00"}}""",
        $$$"""{"resourceType":"MedicationDispense","id":"md1","status":"completed","subject":{"reference":"Patient/m1"},"medicationCodeableConcept":{"text":"Furosemide"},"whenHandedOver":"2150-01-05T00:00:00-05:00"}"""));

    private static string Condition(string id, string code, string display, string encounter) =>
        $$$"""{"resourceType":"Condition","id":"{{{id}}}","category":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-category","code":"encounter-diagnosis"}]}],"code":{"coding":[{"system":"{{{Mimic}}}/CodeSystem/mimic-diagnosis-icd9","code":"{{{code}}}","display":"{{{display}}}"}]},"subject":{"reference":"Patient/m1"},"encounter":{"reference":"Encounter/{{{encounter}}}"}}""";

    private static string Encounter(string id, string classCode, string start, int locations)
    {
        string stays = string.Join(',', Enumerable.Range(1, locations).Select(stay => $$$"""{"location":{"reference":"Location/l{{{stay}}}"}}"""));
        return $$$"""{"resourceType":"Encounter","id":"{{{id}}}","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"{{{classCode}}}"},"subject":{"reference":"Patient/m1"},"period":{"start":"{{{start}}}"},"location":[{{{stays}}}]}""";
    }

    private static string ChartEvent(string id, string code, string display, string category, string date, string value) =>
        $$$"""{"resourceType":"Observation","id":"{{{id}}}","status":"final","category":[{"coding":[{"system":"{{{Mimic}}}/CodeSystem/mimic-observation-category","code":"{{{category}}}"}]}],"code":{"coding":[{"system":"{{{Mimic}}}/CodeSystem/mimic-chartevents-d-items","code":"{{{code}}}","display":"{{{display}}}"}]},"subject":{"reference":"Patient/m1"},"effectiveDateTime":"{{{date}}}",{{{value}}}}""";

    private static string Administration(string id, string date) =>
        $$$"""{"resourceType":"MedicationAdministration","id":"{{{id}}}","status":"completed","subject":{"reference":"Patient/m1"},"medicationCodeableConcept":{"coding":[{"system":"{{{Mimic}}}/CodeSystem/mimic-medication-name","code":"Furosemide"}]},"effectiveDateTime":"{{{date}}}"}""";
}
