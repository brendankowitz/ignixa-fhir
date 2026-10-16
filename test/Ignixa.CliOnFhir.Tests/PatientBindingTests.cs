// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Tools;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Patient-tool statuses checked against their required bindings once terminology is loaded (Task 9, carried from Task 6).
/// Each test is a regression, failing at acdd94f, where a code-shaped status outside its ValueSet was shown as written and
/// the facts did not say whether binding membership was checked.
/// </summary>
public class PatientBindingTests
{
    private const string Id = "example.statuses";
    private const string Version = "1.0.0";
    private const string MedicationStatus = "http://hl7.org/fhir/ValueSet/medicationrequest-status|4.0.1";
    private const string EncounterStatus = "http://hl7.org/fhir/ValueSet/encounter-status|4.0.1";

    [Fact]
    public async Task GivenTerminologyLoaded_WhenAWellShapedStatusIsOutsideItsRequiredValueSet_ThenItIsADigestAndAnIssueNamesTheElementAndValueSetNotTheValue()
    {
        using TestFolder folder = Chart();
        using var store = new TestFolder();
        FolderSession session = WithStatuses(folder, store);
        _ = await TermList.ListAsync(session, null, null, null, null, 0, false, CancellationToken.None);

        JsonNode node = Json(ContextExpander.Expand(session, "p1", "medications"));

        Status(node, "MedicationRequest/m1").ShouldBe("active");
        Status(node, "MedicationRequest/m2").ShouldBe(FhirIds.Digest("paused"));
        ((string)node["facts"]!["valueBindings"]!).ShouldBe("checked");
        JsonNode issue = node["issues"]!.AsArray().Single(item => (string)item!["code"]! == "invalid-value")!;
        ((string)issue["diagnostics"]!).ShouldBe($"Some MedicationRequest status values are not in the required ValueSet {MedicationStatus}, so summaries show each as a digest ('~' and 16 hex digits); the resources hold them as written.");
        node["facts"]!.AsObject().Remove("resources");
        node.ToJsonString().ShouldNotContain("paused", Case.Sensitive);
    }

    [Fact]
    public async Task GivenTerminologyLoadedWithoutAStatusValueSet_WhenItsStatusIsShown_ThenItIsShownAsWrittenWithANotCheckedNote()
    {
        using TestFolder folder = Chart();
        using var store = new TestFolder();
        FolderSession session = WithStatuses(folder, store);
        _ = await TermList.ListAsync(session, null, null, null, null, 0, false, CancellationToken.None);

        JsonNode node = Json(ContextExpander.Expand(session, "p1", "encounters"));

        Status(node, "Encounter/e1").ShouldBe("finished");
        JsonNode note = node["issues"]!.AsArray().Single(item => (string)item!["code"]! == "binding-not-checked")!;
        ((string)note["severity"]!).ShouldBe("information");
        ((string)note["diagnostics"]!).ShouldContain($"Encounter status values are in the required ValueSet {EncounterStatus}");
    }

    [Fact]
    public void GivenNoTerminologyLoaded_WhenAStatusIsShown_ThenOnlyItsShapeIsCheckedAndTheFactsSaySo()
    {
        using TestFolder folder = Chart();
        using var store = new TestFolder();
        FolderSession session = WithStatuses(folder, store);

        JsonNode node = Json(ContextExpander.Expand(session, "p1", "medications"));
        JsonNode root = Json(ContextRoot.Get(session, "p1"));
        JsonNode list = Json(PatientLister.List(session));

        Status(node, "MedicationRequest/m2").ShouldBe("paused");
        ((string)node["facts"]!["valueBindings"]!).ShouldBe("not-checked");
        ((string)root["facts"]!["valueBindings"]!).ShouldBe("not-checked");
        ((string)list["facts"]!["valueBindings"]!).ShouldBe("not-checked");
        session.Terminology.Current.ShouldBeNull();
    }

    private static TestFolder Chart() => new TestFolder()
        .Write("chart.ndjson", """
            {"resourceType":"Patient","id":"p1","gender":"female","birthDate":"1975-03-04"}
            {"resourceType":"MedicationRequest","id":"m1","status":"active","intent":"order","subject":{"reference":"Patient/p1"},"medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"435","display":"Albuterol"}]},"authoredOn":"2020-05-02"}
            {"resourceType":"MedicationRequest","id":"m2","status":"paused","intent":"order","subject":{"reference":"Patient/p1"},"medicationCodeableConcept":{"coding":[{"system":"http://www.nlm.nih.gov/research/umls/rxnorm","code":"860975","display":"Metformin"}]},"authoredOn":"2018-01-02"}
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/p1"},"period":{"start":"2020-01-01T10:00:00Z"}}
            """);

#pragma warning disable CA2000 // The registry client owns the stub handler and lives as long as the session, as in the tool.
    private static FolderSession WithStatuses(TestFolder folder, TestFolder store)
    {
        byte[] archive = Archive(PackageFiles(
            Id,
            Version,
            "4.0.1",
            CodeSystem("http://hl7.org/fhir/CodeSystem/medicationrequest-status", "4.0.1", string.Empty, "active", "on-hold", "cancelled", "completed", "entered-in-error", "stopped", "draft", "unknown"),
            """{"resourceType":"ValueSet","id":"medicationrequest-status","url":"http://hl7.org/fhir/ValueSet/medicationrequest-status","version":"4.0.1","compose":{"include":[{"system":"http://hl7.org/fhir/CodeSystem/medicationrequest-status"}]}}""",
            CodeSystem("http://hl7.org/fhir/administrative-gender", "4.0.1", string.Empty, "male", "female", "other", "unknown"),
            """{"resourceType":"ValueSet","id":"administrative-gender","url":"http://hl7.org/fhir/ValueSet/administrative-gender","version":"4.0.1","compose":{"include":[{"system":"http://hl7.org/fhir/administrative-gender"}]}}"""));
        TerminologyOptions options = Online(Registry([(Id, Version, archive, null)]), store, $"{Id}@{Version}");
        return Open(folder, options);
    }
#pragma warning restore CA2000

    private static string Status(JsonNode node, string reference) =>
        (string)node["facts"]!["items"]!.AsArray().Single(item => (string)item!["reference"]! == reference)!["status"]!;
}
