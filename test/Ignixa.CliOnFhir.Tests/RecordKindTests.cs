// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Inspection;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Record kinds (V17). MIMIC-IV on FHIR keeps hospital, ED and ICU encounters in one type, told apart only by identifier.system
/// (encounter-hosp, encounter-ed, encounter-icu), and chart, lab and output events in one type, told apart by meta.profile. At
/// V16 a search over the type mixed them silently: "hospital visits" counted ED and ICU stays (4 for a gold of 2) and "urine
/// output events" counted chart events.
/// </summary>
public class RecordKindTests
{
    private const string Sd = "http://mimic.mit.edu/fhir/mimic/StructureDefinition/";
    private const string Id = "http://mimic.mit.edu/fhir/mimic/identifier/";

    [Fact]
    public void GivenMimicShapedEncounters_WhenKindsAreCounted_ThenHospitalEdAndIcuAreTheirOwnKindsByIdentifierSystem()
    {
        using TestFolder folder = Hospital();

        IReadOnlyList<FolderKinds.KindCount> kinds = Open(folder).Kinds.Of("Encounter");

        kinds.Select(kind => (kind.ShortName, kind.Count, kind.Filter)).ShouldBe([
            ("hosp", 2, $"identifier={Id}encounter-hosp|"),
            ("ed", 1, $"identifier={Id}encounter-ed|"),
            ("icu", 1, $"identifier={Id}encounter-icu|")]);
    }

    [Fact]
    public void GivenMimicShapedObservations_WhenKindsAreCounted_ThenEachProfileIsAKindNamedWithoutTheSharedWords()
    {
        using TestFolder folder = Hospital();

        IReadOnlyList<FolderKinds.KindCount> kinds = Open(folder).Kinds.Of("Observation");

        kinds.Select(kind => (kind.ShortName, kind.Count, kind.Filter)).ShouldBe([
            ("chartevents", 2, $"_profile={Sd}mimic-observation-chartevents"),
            ("outputevents", 1, $"_profile={Sd}mimic-observation-outputevents")]);
    }

    [Fact]
    public void GivenAPatientsEncounters_WhenSearched_ThenTheResultsSayWhichKindsTheyMixAndEachFilterKeepsOne()
    {
        using TestFolder folder = Hospital();
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> mixed = ResourceSearcher.Search(session, new SearchRequest("Encounter", "date=ge2125-01-01&date=lt2126-01-01", Compartment: "Patient/p1"));
        IssueItem note = mixed.Issues.ShouldHaveSingleItem();
        ToolEnvelope<SearchFacts?> hospital = ResourceSearcher.Search(session, new SearchRequest("Encounter", $"date=ge2125-01-01&identifier={Id}encounter-hosp|", Compartment: "Patient/p1", Summary: "count"));

        mixed.Facts!.Total.ShouldBe(4);
        note.Code.ShouldBe(SearchKinds.MixedCode);
        note.Severity.ShouldBe("information");
        note.Diagnostics.ShouldContain($"mix 3 kinds of Encounter: hosp 2 [identifier={Id}encounter-hosp|], ed 1 [identifier={Id}encounter-ed|], icu 1 [identifier={Id}encounter-icu|]");
        hospital.Facts!.Total.ShouldBe(2);
        hospital.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTheKindColumn_WhenSearchedWithColumns_ThenEachRowHasItsKindsShortName()
    {
        using TestFolder folder = Hospital();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(
            Open(folder),
            new SearchRequest("Encounter", "_sort=date", Columns: [new SearchColumn("kind", "%kind"), new SearchColumn("class", "class.code")]));

        envelope.Facts!.Rows!.Select(row => ((string)row![0]!, (string)row[1]!, (string)row[2]!)).ShouldBe([
            ("Encounter/ed1", "ed", "EMER"),
            ("Encounter/h1", "hosp", "EMER"),
            ("Encounter/icu1", "icu", "ACUTE"),
            ("Encounter/h2", "hosp", "IMP")]);
        envelope.Issues.Select(issue => issue.Code).ShouldBe([SearchKinds.MixedCode]);
    }

    [Theory]
    [InlineData("outputevents")]
    [InlineData("mimic-observation-outputevents")]
    public void GivenAProfilesShortName_WhenSearchedByProfile_ThenItIsReadAsTheCanonicalWithAWarning(string name)
    {
        using TestFolder folder = Hospital();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", $"_profile={name}"));

        Ids(envelope).ShouldBe(["u1"]);
        IssueItem warning = envelope.Issues.ShouldHaveSingleItem();
        warning.Code.ShouldBe(LenientArguments.Code);
        warning.Diagnostics.ShouldContain($"_profile={Sd}mimic-observation-outputevents");
    }

    [Fact]
    public void GivenTheFullCanonical_WhenSearchedByProfile_ThenOnlyThatKindMatchesWithoutAWarning()
    {
        using TestFolder folder = Hospital();

        ToolEnvelope<SearchFacts?> envelope = ResourceSearcher.Search(Open(folder), new SearchRequest("Observation", $"_profile={Sd}mimic-observation-chartevents"));

        Ids(envelope).ShouldBe(["c1", "c2"]);
        envelope.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAMimicShapedRecord_WhenSummarized_ThenKindsCountEachMixedTypeForThePatient()
    {
        using TestFolder folder = Hospital();

        PatientMapFacts facts = PatientMap.Summarize(Open(folder), "10000001").Facts.ShouldNotBeNull();

        facts.Kinds.ShouldNotBeNull().Keys.ShouldBe(["Encounter", "Observation"]);
        facts.Kinds["Encounter"].ShouldBe(new Dictionary<string, int> { ["hosp"] = 2, ["ed"] = 1, ["icu"] = 1 });
        facts.Kinds["Observation"].ShouldBe(new Dictionary<string, int> { ["chartevents"] = 2, ["outputevents"] = 1 });
    }

    [Fact]
    public async Task GivenAMimicShapedFolder_WhenItsOverviewIsAsked_ThenEachMixedTypeListsItsKinds()
    {
        using TestFolder folder = Hospital();
        FolderSession session = Open(folder);

        CodeSystemsCompactFacts systems = (await FolderCodeSystems.ListCompactAsync(session, null, 0, null, withTerminology: false, CancellationToken.None)).Facts.ShouldNotBeNull();
        FolderInspection inspection = (await FolderInspector.InspectAsync(session, FolderInspector.DefaultIssueLimit, buildIndexes: false, withTerminology: false, CancellationToken.None)).Facts;

        systems.Kinds.ShouldNotBeNull().Keys.ShouldBe(["Encounter", "Observation"]);
        systems.Kinds["Encounter"].ShouldBe(new Dictionary<string, int> { ["hosp"] = 2, ["ed"] = 1, ["icu"] = 1 });
        inspection.ResourceTypes.Single(type => type.ResourceType == "Observation").Kinds.ShouldNotBeNull()
            .ShouldBe([new KindFacts("chartevents", 2, $"_profile={Sd}mimic-observation-chartevents"), new KindFacts("outputevents", 1, $"_profile={Sd}mimic-observation-outputevents")]);
        inspection.ResourceTypes.Single(type => type.ResourceType == "Patient").Kinds.ShouldBeNull();
    }

    [Fact]
    public async Task GivenOneKindPerType_WhenSearchedSummarizedAndOverviewed_ThenNothingAboutKindsIsAdded()
    {
        using TestFolder folder = new TestFolder()
            .Write("p.ndjson", """{"resourceType":"Patient","id":"p1","meta":{"profile":["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]},"identifier":[{"system":"http://hospital/mrn","value":"1"}]}""")
            .Write("e.ndjson", """
                {"resourceType":"Encounter","id":"e1","meta":{"profile":["http://hl7.org/fhir/us/core/StructureDefinition/us-core-encounter"]},"status":"finished","class":{"code":"AMB"},"subject":{"reference":"Patient/p1"},"period":{"start":"2020-01-01"}}
                {"resourceType":"Encounter","id":"e2","meta":{"profile":["http://hl7.org/fhir/us/core/StructureDefinition/us-core-encounter"]},"status":"finished","class":{"code":"EMER"},"subject":{"reference":"Patient/p1"},"period":{"start":"2020-02-01"}}
                """);
        FolderSession session = Open(folder);

        ToolEnvelope<SearchFacts?> search = ResourceSearcher.Search(session, new SearchRequest("Encounter", null));
        PatientMapFacts map = PatientMap.Get(session, "p1").Facts.ShouldNotBeNull();
        CodeSystemsCompactFacts systems = (await FolderCodeSystems.ListCompactAsync(session, null, 0, null, withTerminology: false, CancellationToken.None)).Facts.ShouldNotBeNull();

        search.Issues.ShouldBeEmpty();
        map.Kinds.ShouldBeNull();
        systems.Kinds.ShouldBeNull();
        JsonSerializer.Serialize(map, EnvelopeJson.Compact).ShouldNotContain("kinds");
    }

    [Fact]
    public void GivenResourcesWithoutProfiles_WhenKindsAreCounted_ThenEncountersFallBackToClassAndObservationsToCategory()
    {
        using TestFolder folder = new TestFolder()
            .Write("e.ndjson", """
                {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"}}
                {"resourceType":"Encounter","id":"e2","status":"finished","class":[{"coding":[{"code":"IMP"}]}]}
                {"resourceType":"Encounter","id":"e3","status":"finished"}
                """)
            .Write("o.ndjson", """
                {"resourceType":"Observation","id":"o1","status":"final","category":[{"coding":[{"code":"laboratory"}]}],"code":{"text":"x"}}
                {"resourceType":"Observation","id":"o2","status":"final","category":[{"coding":[{"code":"vital-signs"}]},{"coding":[{"code":"vital-signs"}]}],"code":{"text":"x"}}
                """)
            .Write("c.ndjson", """{"resourceType":"Condition","id":"c1","category":[{"coding":[{"code":"problem-list-item"}]}],"subject":{"reference":"Patient/p"}}""");

        FolderKinds kinds = Open(folder).Kinds;

        kinds.Of("Encounter").Select(kind => (kind.ShortName, kind.Filter)).Order().ShouldBe([("(none)", null), ("class:AMB", "class=AMB"), ("class:IMP", "class=IMP")]);
        kinds.Of("Observation").Select(kind => (kind.ShortName, kind.Filter)).ShouldBe([("category:laboratory", "category=laboratory"), ("category:vital-signs", "category=vital-signs")]);
        kinds.Of("Condition").ShouldHaveSingleItem().Kind.ShouldBeNull();
    }

    [Fact]
    public void GivenSeveralOrRepeatedProfiles_WhenSniffed_ThenTheKindIsTheDistinctProfilesInOrder()
    {
        byte[] json = """{"meta":{"lastUpdated":"2020","profile":["http://a/x","http://b/y","http://a/x"]},"resourceType":"Observation","id":"o1","category":[{"coding":[{"code":"laboratory"}]}],"identifier":[{"system":"http://s","value":"1"},{"system":"http://s","value":"2"}]}"""u8.ToArray();

        (string? type, string? id, string? kind, string? system) = ResourceSniffer.SniffWithKind(json);

        (type, id, kind, system).ShouldBe(("Observation", "o1", "http://a/x http://b/y", "http://s"));
        RecordKind.ShortName(kind!).ShouldBe("x+y");
        RecordKind.Filter(kind, null).ShouldBe("_profile=http://a/x");
    }

    [Fact]
    public void GivenIdentifiersOfSeveralSystems_WhenKindsAreCounted_ThenTheKindIsNotSplit()
    {
        using TestFolder folder = new TestFolder().Write("p.ndjson", """
            {"resourceType":"Patient","id":"a","meta":{"profile":["http://p/patient"]},"identifier":[{"system":"http://hospital/mrn","value":"1"},{"system":"http://ssn","value":"2"}]}
            {"resourceType":"Patient","id":"b","meta":{"profile":["http://p/patient"]},"identifier":[{"system":"http://hospital/mrn","value":"3"}]}
            """);

        Open(folder).Kinds.IsMixed("Patient").ShouldBeFalse();
    }

    /// <summary>
    /// Patient p1 (MRN 10000001) shaped as MIMIC-IV on FHIR 2.1: two hospital admissions, one ED visit and one ICU stay, all with
    /// the mimic-encounter profile and told apart by identifier system; two chart events and one output event.
    /// </summary>
    internal static TestFolder Hospital() => new TestFolder()
        .Write("MimicPatient.ndjson", $$$"""{"resourceType":"Patient","id":"p1","meta":{"profile":["{{{Sd}}}mimic-patient"]},"identifier":[{"system":"{{{Id}}}patient","value":"10000001"}],"gender":"female","birthDate":"2080-01-01"}""")
        .Write("MimicEncounter.ndjson", $$$"""
            {"resourceType":"Encounter","id":"h1","meta":{"profile":["{{{Sd}}}mimic-encounter"]},"identifier":[{"system":"{{{Id}}}encounter-hosp","value":"1"}],"status":"finished","class":{"code":"EMER"},"subject":{"reference":"Patient/p1"},"period":{"start":"2125-01-01T08:00:00-05:00"}}
            {"resourceType":"Encounter","id":"h2","meta":{"profile":["{{{Sd}}}mimic-encounter"]},"identifier":[{"system":"{{{Id}}}encounter-hosp","value":"2"}],"status":"finished","class":{"code":"IMP"},"subject":{"reference":"Patient/p1"},"period":{"start":"2125-03-01T08:00:00-05:00"}}
            """)
        .Write("MimicEncounterED.ndjson", $$$"""{"resourceType":"Encounter","id":"ed1","meta":{"profile":["{{{Sd}}}mimic-encounter"]},"identifier":[{"system":"{{{Id}}}encounter-ed","value":"3"}],"status":"finished","class":{"code":"EMER"},"subject":{"reference":"Patient/p1"},"period":{"start":"2125-01-01T05:00:00-05:00"},"partOf":{"reference":"Encounter/h1"}}""")
        .Write("MimicEncounterICU.ndjson", $$$"""{"resourceType":"Encounter","id":"icu1","meta":{"profile":["{{{Sd}}}mimic-encounter"]},"identifier":[{"system":"{{{Id}}}encounter-icu","value":"4"}],"status":"finished","class":{"code":"ACUTE"},"subject":{"reference":"Patient/p1"},"period":{"start":"2125-01-02T08:00:00-05:00"},"partOf":{"reference":"Encounter/h1"}}""")
        .Write("MimicObservationChartevents.ndjson", $$$"""
            {"resourceType":"Observation","id":"c1","meta":{"profile":["{{{Sd}}}mimic-observation-chartevents"]},"status":"final","category":[{"coding":[{"code":"Routine Vital Signs"}]}],"code":{"coding":[{"system":"http://mimic.mit.edu/fhir/mimic/CodeSystem/mimic-chartevents-d-items","code":"220045"}]},"subject":{"reference":"Patient/p1"},"encounter":{"reference":"Encounter/icu1"},"effectiveDateTime":"2125-01-02T09:00:00-05:00","valueQuantity":{"value":80}}
            {"resourceType":"Observation","id":"c2","meta":{"profile":["{{{Sd}}}mimic-observation-chartevents"]},"status":"final","category":[{"coding":[{"code":"Routine Vital Signs"}]}],"code":{"coding":[{"system":"http://mimic.mit.edu/fhir/mimic/CodeSystem/mimic-chartevents-d-items","code":"220045"}]},"subject":{"reference":"Patient/p1"},"encounter":{"reference":"Encounter/icu1"},"effectiveDateTime":"2125-01-02T10:00:00-05:00","valueQuantity":{"value":82}}
            """)
        .Write("MimicObservationOutputevents.ndjson", $$$"""{"resourceType":"Observation","id":"u1","meta":{"profile":["{{{Sd}}}mimic-observation-outputevents"]},"status":"final","category":[{"coding":[{"code":"Output"}]}],"code":{"coding":[{"system":"http://mimic.mit.edu/fhir/mimic/CodeSystem/mimic-itemid","code":"226559"}]},"subject":{"reference":"Patient/p1"},"encounter":{"reference":"Encounter/icu1"},"effectiveDateTime":"2125-01-02T11:00:00-05:00","valueQuantity":{"value":300,"unit":"mL"}}""");
}
