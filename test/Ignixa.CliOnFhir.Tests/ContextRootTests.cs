// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.Specification.Generated;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.PatientFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>context_get_root</c>. New capability: at 523e80d no patient tool existed, so every test here fails there (it does not
/// compile). Each runs over the chart as NDJSON and as a urn:uuid transaction Bundle.
/// </summary>
public class ContextRootTests
{
    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenActiveAndInactiveConditions_WhenTheRootIsRead_ThenOnlyActiveOnesAreProblemsAndTheRuleIsStated(string layout)
    {
        using TestFolder folder = Layout(layout);

        RootDigestFacts facts = Root(folder);

        // c2 is resolved; c3 (recurrence) counts as active; newest onset first.
        facts.Problems.Items.Select(item => item.Reference).ShouldBe(["Condition/c1", "Condition/c3"]);
        facts.Problems.Count.ShouldBe(2);
        facts.Problems.Excluded.ShouldBe(1);
        facts.Problems.Rule.ShouldNotBeNull().ShouldContain("active, recurrence or relapse");
        facts.Problems.Items[0].NodeKey.ShouldBe("problem/Condition-c1");
    }

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenNoAllergyIntolerance_WhenTheRootIsRead_ThenAnAbsentEntryStatesTheCompartmentChecked(string layout)
    {
        // p2 has an allergy, so the folder holds one; p1 has none.
        using TestFolder folder = Layout(layout);

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), "p1");

        envelope.Facts.ShouldNotBeNull().Allergies.Count.ShouldBe(0);
        AbsentItem absent = envelope.Absent.Where(item => item.What == "AllergyIntolerance").ShouldHaveSingleItem();
        absent.Checked.ShouldStartWith("Patient/p1 compartment (32 resources, via asserter, patient, recorder); 1 in the folder's 1 files.");
        absent.Checked.ShouldContain("not 'no known allergies'");
        absent.IndexBuiltAt.ShouldBe(envelope.Freshness.IndexBuiltAt!.Value);
    }

    [Fact]
    public void GivenAnAllergy_WhenTheRootIsRead_ThenItIsListedAndNothingIsAbsent()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), "p2");

        envelope.Facts.ShouldNotBeNull().Allergies.Items.ShouldHaveSingleItem().Display.ShouldBe("Peanut");
        envelope.Absent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenLabSeries_WhenTheRootIsRead_ThenEachCodeHasItsLatestValueAndTrend(string layout)
    {
        using TestFolder folder = Layout(layout);

        DomainStub labs = Root(folder).Labs;

        // Heart rate has the most observations; ties go by key. Glucose after asOf (o10) and entered-in-error (o11) and the
        // survey (o12) are left out.
        string[] rows = [.. labs.Items.Select(item => $"{item.Code} {item.Count} {item.Value} {item.Unit} {item.Date} {item.Trend} {item.Reference}")];
        rows.Take(4).ShouldBe([
            "http://loinc.org|8867-4 4 80 /min 2025-01-01 up Observation/o3",
            "http://loinc.org|2339-0 3 100 mg/dL 2025-01-01 down Observation/o9",
            "http://loinc.org|8480-6 3 121 mm[Hg] 2025-01-01 flat Observation/o6",
            "http://loinc.org|72166-2 1   2025-01-01 n/a Observation/o13"]);
        labs.Items[3].ValueType.ShouldBe("CodeableConcept");
        labs.Count.ShouldBe(5);
        labs.Excluded.ShouldBe(3);
        labs.Window.ShouldBe(new ContextWindow(null, AsOf));
    }

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenEncountersAroundAsOf_WhenTheRootIsRead_ThenTheFiveNewestRecordedOnesUpToAsOfAreShown(string layout)
    {
        // e6 is entered-in-error and e8 starts after asOf.
        using TestFolder folder = Layout(layout);

        DomainStub encounters = Root(folder).Encounters;

        encounters.Items.Select(item => item.Reference).ShouldBe(["Encounter/e7", "Encounter/e5", "Encounter/e4", "Encounter/e3", "Encounter/e2"]);
        encounters.Count.ShouldBe(6);
        encounters.Excluded.ShouldBe(2);
        encounters.Window.ShouldBe(new ContextWindow(null, AsOf));
    }

    [Fact]
    public void GivenAnEarlierClock_WhenTheRootIsRead_ThenTheTimeDependentDomainsAreAnchoredToIt()
    {
        using TestFolder folder = Ndjson();
        var earlier = new DateTimeOffset(2024, 6, 1, 8, 30, 15, 500, TimeSpan.FromHours(2));

        RootDigestFacts facts = ContextRoot.Get(Open(folder, earlier), "p1").Facts.ShouldNotBeNull();

        // Truncated to the second, in UTC.
        facts.AsOf.ShouldBe(new DateTimeOffset(2024, 6, 1, 6, 30, 15, TimeSpan.Zero));
        facts.Encounters.Items[0].Reference.ShouldBe("Encounter/e5");
        facts.Labs.Items[0].Reference.ShouldBe("Observation/o2");
        facts.Labs.Items[0].Trend.ShouldBe("up");
        facts.Encounters.Window!.To.ShouldBe(facts.AsOf);
    }

    [Fact]
    public void GivenAReferencedMedication_WhenTheRootIsRead_ThenItsDisplayComesFromTheMedicationAndAMissingOneIsAbsent()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), "p1");

        DomainStub medications = envelope.Facts.ShouldNotBeNull().Medications;
        medications.Items.Select(item => $"{item.Reference} {item.Display}").ShouldBe([
            "MedicationRequest/m6 ",
            "MedicationRequest/m5 Aspirin",
            "MedicationRequest/m1 Albuterol",
            "MedicationRequest/m3 Metformin"]);
        medications.Excluded.ShouldBe(1);
        envelope.Evidence.Select(item => item.Reference).ShouldContain("Medication/med1");
        envelope.Absent.ShouldContain(item => item.What == "Medication/missing" && item.Checked.EndsWith(": 1 Medication resources", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenNarrativeNotesAndStringValues_WhenTheRootIsRead_ThenNoneOfThatTextIsInTheResponse(string layout)
    {
        using TestFolder folder = Layout(layout);

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), "p1");

        JsonSerializer.Serialize(envelope, EnvelopeJson.Compact).ShouldNotContain(Hostile);
    }

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenAHostileCode_WhenTheRootIsRead_ThenItsNodeKeyIsADigestAndNextCarriesOnlyStructure(string layout)
    {
        using TestFolder folder = Layout(layout);

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), "p1");

        ContextItem hostile = envelope.Facts.ShouldNotBeNull().Labs.Items.Where(item => item.Reference == "Observation/o14").ShouldHaveSingleItem();
        hostile.NodeKey.ShouldNotBeNull().ShouldMatch("^labs/~[0-9a-f]{16}\\|~[0-9a-f]{16}$");
        hostile.ValueType.ShouldBe("string");
        envelope.Next.Select(call => $"{call.Tool} {string.Join(",", call.Args.Select(arg => $"{arg.Key}={arg.Value}"))}").ShouldBe([
            "context_expand patientId=p1,nodeKey=problems",
            "context_expand patientId=p1,nodeKey=medications",
            "context_expand patientId=p1,nodeKey=encounters",
            "context_expand patientId=p1,nodeKey=labs"]);
    }

    [Fact]
    public void GivenCompartmentTypesNoStubCovers_WhenTheRootIsRead_ThenTheyAreCountedAndNextSearchesThem()
    {
        using TestFolder folder = Ndjson().Write("imm.ndjson", """
            {"resourceType":"Immunization","id":"i1","status":"completed","vaccineCode":{"text":"Flu"},"patient":{"reference":"Patient/p1"},"occurrenceDateTime":"2024-10-01"}
            """);

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), "p1");

        envelope.Facts.ShouldNotBeNull().OtherTypes.ShouldBe(new Dictionary<string, int> { ["Immunization"] = 1 });
        NextCall search = envelope.Next.Where(call => call.Tool == "search_fhir_resources").ShouldHaveSingleItem();
        search.Args.ShouldBe(new Dictionary<string, object> { ["resourceType"] = "Immunization", ["compartment"] = "Patient/p1" }, ignoreOrder: true);
    }

    [Fact]
    public void GivenTheDemographics_WhenTheRootIsRead_ThenTheOfficialNameIsUsedAndThePatientIsTheFirstEvidence()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), "p1");

        envelope.Facts.ShouldNotBeNull().Demographics.ShouldBe(new Demographics("Patient/p1", "Ann B Smith", "female", "1975-03-04", null));
        envelope.Facts.CompartmentSize.ShouldBe(32);
        envelope.Evidence[0].Reference.ShouldBe("Patient/p1");
        envelope.Issues.ShouldBeEmpty();
    }

    // Guard (size budget): a Synthea-shaped patient (UUID ids, long file names, five encounters and lab codes) stays within
    // what the sample patients measure; the design's 5 KB target is not met at this shape (see the Task 6 report).
    [Fact]
    public void GivenASyntheaShapedPatient_WhenTheRootIsRead_ThenTheResponseStaysWithinTheMeasuredBudget()
    {
        using TestFolder folder = SyntheaShaped();

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), Uuid("patient"));

        envelope.Facts.ShouldNotBeNull().Labs.Items.Count.ShouldBe(5);
        envelope.Facts.Encounters.Items.Count.ShouldBe(5);
        EnvelopeJson.SizeOf(envelope).ShouldBeLessThan(10 * 1024);
    }

    [Theory]
    [InlineData("p9", "unknown-patient", "The folder holds no Patient/p9 (3 Patient resources).")]
    [InlineData("p 1", "invalid-parameter", "'patientId' must be a Patient id")]
    public void GivenAnUnknownOrMalformedPatient_WhenTheRootIsRequested_ThenTheCallFailsWithARequestError(string patientId, string code, string diagnostics)
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder), patientId);

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe("error");
        issue.Code.ShouldBe(code);
        issue.Diagnostics.ShouldStartWith(diagnostics);
    }

    [Fact]
    public void GivenAFolderReadAsR5_WhenTheRootIsRead_ThenTheRulesAreNotAppliedAndAWarningSaysSo()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder, schema: new R5CoreSchemaProvider()), "p1");

        RootDigestFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Problems.Count.ShouldBe(3);
        facts.Problems.Rule.ShouldBeNull();
        facts.Problems.Excluded.ShouldBeNull();
        IssueItem issue = envelope.Issues.Where(item => item.Code == "domain-rule-unsupported-version").ShouldHaveSingleItem();
        issue.Severity.ShouldBe("warning");
        issue.Diagnostics.ShouldContain("written for R4 and this folder is read as R5");
    }

    // Guard: R5's Patient compartment has {def}, so the Patient is a member there too; it is counted once.
    [Fact]
    public void GivenTheR5PatientCompartment_WhenTheRootIsRead_ThenThePatientIsCountedOnce()
    {
        using TestFolder folder = Ndjson();

        RootDigestFacts facts = ContextRoot.Get(Open(folder, schema: new R5CoreSchemaProvider()), "p1").Facts.ShouldNotBeNull();

        facts.CompartmentSize.ShouldBe(32);
    }

    [Fact]
    public void GivenAnStu3Folder_WhenTheRootIsRead_ThenItIsAnsweredWithWarningsForWhatStu3CannotDo()
    {
        // STU3 lists Provenance codes that are not search parameters; Condition.clinicalStatus is a plain code there.
        using TestFolder folder = new TestFolder().Write("chart.ndjson", """
            {"resourceType":"Patient","id":"p1"}
            {"resourceType":"Provenance","id":"pv1","target":[{"reference":"Patient/p1"}],"recorded":"2020-01-01T00:00:00Z","agent":[{"whoReference":{"reference":"Patient/p1"}}]}
            {"resourceType":"Condition","id":"c1","clinicalStatus":"active","code":{"coding":[{"system":"http://snomed.info/sct","code":"195967001","display":"Asthma"}]},"subject":{"reference":"Patient/p1"},"onsetDateTime":"2020-01-01"}
            """);

        ToolEnvelope<RootDigestFacts?> envelope = ContextRoot.Get(Open(folder, schema: new STU3CoreSchemaProvider()), "p1");

        envelope.Facts.ShouldNotBeNull().Problems.Items.ShouldHaveSingleItem().Display.ShouldBe("Asthma");
        envelope.Issues.Select(item => $"{item.Severity} {item.Code}").ShouldBe([
            "warning domain-rule-unsupported-version",
            "warning compartment-type-unevaluable"]);
        envelope.Issues[1].Diagnostics.ShouldStartWith("Provenance resources in the folder were not considered for Patient/p1's compartment");
    }

    private static RootDigestFacts Root(TestFolder folder) => ContextRoot.Get(Open(folder), "p1").Facts.ShouldNotBeNull();

    private static string Uuid(string name) => new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)).AsSpan(0, 16)).ToString();

    /// <summary>One patient shaped like the samples: long file names, UUID ids, two problems and medications, 30 encounters and 8 lab codes.</summary>
    private static TestFolder SyntheaShaped()
    {
        string patient = Uuid("patient");
        string prefix = "r4-population-Massachusetts-";
        string suffix = "-40-f7021b5a-9fbb-4b52-a415-35f83c46ca82.ndjson";
        var folder = new TestFolder()
            .Write($"{prefix}Patient{suffix}", $$"""{"resourceType":"Patient","id":"{{patient}}","name":[{"use":"official","family":"Kutch","given":["Allen"]}],"gender":"male","birthDate":"1974-06-27"}""")
            .Write($"{prefix}Condition{suffix}", string.Join('\n', Enumerable.Range(1, 2).Select(i =>
                $$"""{"resourceType":"Condition","id":"{{Uuid($"c{i}")}}","clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"active"}]},"code":{"coding":[{"system":"http://snomed.info/sct","code":"4405400{{i}}","display":"Diabetes mellitus type {{i}}"}],"text":"Diabetes mellitus type {{i}}"},"subject":{"reference":"Patient/{{patient}}"},"onsetDateTime":"201{{i}}-01-01T00:00:00.0000000"}""")))
            .Write($"{prefix}MedicationRequest{suffix}", string.Join('\n', Enumerable.Range(1, 2).Select(i =>
                $$"""{"resourceType":"MedicationRequest","id":"{{Uuid($"m{i}")}}","status":"active","intent":"order","subject":{"reference":"Patient/{{patient}}"},"medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"86097{{i}}","display":"Metformin hydrochloride 500 MG Oral Tablet"}]},"authoredOn":"201{{i}}-01-01T00:00:00.0000000"}""")))
            .Write($"{prefix}Encounter{suffix}", string.Join('\n', Enumerable.Range(1, 30).Select(i =>
                $$$"""{"resourceType":"Encounter","id":"{{{Uuid($"e{i}")}}}","status":"finished","class":{"code":"AMB"},"type":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"WELLNESS","display":"wellness visit"}]}],"subject":{"reference":"Patient/{{{patient}}}"},"period":{"start":"{{{1990 + i}}}-01-01T00:00:00.0000000","end":"{{{1990 + i}}}-01-01T00:30:00.0000000"}}""")))
            .Write($"{prefix}Observation{suffix}", string.Join('\n', Enumerable.Range(1, 8).SelectMany(code => Enumerable.Range(1, 30).Select(year =>
                $$$"""{"resourceType":"Observation","id":"{{{Uuid($"o{code}-{year}")}}}","status":"final","category":[{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/observation-category","code":"vital-signs"}]}],"code":{"coding":[{"system":"http://loinc.org","code":"2946{{{code}}}-7","display":"Body measurement {{{code}}}"}]},"subject":{"reference":"Patient/{{{patient}}}"},"effectiveDateTime":"{{{1990 + year}}}-01-01T00:00:00.0000000","valueQuantity":{"value":{{{70 + (year % 7)}}}.51703948863604,"unit":"kg","system":"http://unitsofmeasure.org","code":"kg"}}"""))));
        return folder;
    }
}
