// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Validation;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.ValidationFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 9 fix round 2: the reference target-type and profile required-binding gap detectors, and invariant descriptions.
/// Tests marked regression fail at 29aa44d: there a reference to a type the element does not allow was <c>valid</c>, an
/// undeterminable reference was not counted, a text-only concept under a required binding a profile adds passed unchecked,
/// and a profile's invariant reusing a core key got the core meaning. The tests read the envelope as JSON, so they compile there.
/// </summary>
public class ValidateReferenceTargetTests
{
    private const string Rule = "reference-target-type";

    private const string Narrative = """ "text":{"status":"generated","div":"<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>"} """;

    // Regression: at 29aa44d each was valid (or unknown) with no reference-target-type group.
    [Theory]
    [InlineData("""{"resourceType":"Observation","id":"r","status":"final","code":{"text":"x"},"subject":{"reference":"Organization/o1"}}""", "Observation.subject")]
    [InlineData("""{"resourceType":"Patient","id":"r","managingOrganization":{"reference":"Patient/p2"}}""", "Patient.managingOrganization")]
    [InlineData("""{"resourceType":"Patient","id":"r","managingOrganization":{"reference":"https://example.org/fhir/Patient/p2/_history/3"}}""", "Patient.managingOrganization")]
    public async Task GivenAReferenceToATypeTheElementDoesNotAllow_WhenValidated_ThenTheResourceIsInvalidUnderReferenceTargetType(string resource, string path)
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", resource);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(1);
        JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == Rule)!;
        ((string)group["severity"]!).ShouldBe("error");
        ((string)group["path"]!).ShouldBe(path);
        ((string)group["profile"]!).ShouldBe(path.StartsWith("Patient", StringComparison.Ordinal) ? "http://hl7.org/fhir/StructureDefinition/Patient" : "http://hl7.org/fhir/StructureDefinition/Observation");
    }

    // Guard: correct references (relative, absolute, with a matching type) stayed valid before the detector and must still.
    [Fact]
    public async Task GivenReferencesToAllowedTypes_WhenValidated_ThenTheResourcesStayValid()
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", """
            {"resourceType":"Patient","id":"a","managingOrganization":{"reference":"Organization/o1"}}
            {"resourceType":"Patient","id":"b","managingOrganization":{"reference":"https://example.org/fhir/Organization/o1"}}
            {"resourceType":"Patient","id":"c","managingOrganization":{"reference":"Organization/o1","type":"Organization"},"generalPractitioner":[{"reference":"Practitioner/x"},{"reference":"Organization/o1"}]}
            """);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(3);
        Rules(result).ShouldNotContain(Rule);
        Strings(result["facts"]!["against"]!["checksNotRun"]!).ShouldNotContain(item => item.StartsWith(Rule, StringComparison.Ordinal));
    }

    // Regression: at 29aa44d checksNotRun did not count the references whose target type could not be told.
    [Fact]
    public async Task GivenReferencesWhoseTypeCannotBeTold_WhenValidated_ThenTheyAreCountedInChecksNotRunAndTheVerdictIsUnchanged()
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", """
            {"resourceType":"Patient","id":"urn","managingOrganization":{"reference":"urn:uuid:9b1f6c1e-0000-4000-8000-000000000001"}}
            {"resourceType":"Patient","id":"logical","managingOrganization":{"identifier":{"system":"http://example.org/orgs","value":"o1"}}}
            """);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(2);
        Rules(result).ShouldNotContain(Rule);
        Strings(result["facts"]!["against"]!["checksNotRun"]!).ShouldContain("reference-target-type (undeterminable: 2)");
    }

    // Regression: at 29aa44d the urn naming the Bundle's Organization entry was not resolved, so the Observation was not invalid.
    [Fact]
    public async Task GivenABundleUrnReferenceResolvingToAWrongType_WhenValidated_ThenTheEntryIsInvalidAndARightOneIsNot()
    {
        using var folder = new TestFolder();
        folder.Write("bundle.json", """
            {"resourceType":"Bundle","type":"transaction","entry":[
              {"fullUrl":"urn:uuid:org-1","resource":{"resourceType":"Organization","id":"o1","name":"x"},"request":{"method":"POST","url":"Organization"}},
              {"fullUrl":"urn:uuid:pat-1","resource":{"resourceType":"Patient","id":"p1","managingOrganization":{"reference":"urn:uuid:org-1"}},"request":{"method":"POST","url":"Patient"}},
              {"fullUrl":"urn:uuid:pat-2","resource":{"resourceType":"Patient","id":"p2","managingOrganization":{"reference":"urn:uuid:pat-1"}},"request":{"method":"POST","url":"Patient"}}
            ]}
            """);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(ResourceType: "Patient", Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(1);
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == Rule)!;
        Strings(group["samples"]!).ShouldBe(["Patient/p2"]);
    }

    // Regression: at 29aa44d a profile's target profiles were not read, so a reference the core allows but the profile does not passed.
    [Fact]
    public async Task GivenAProfileNarrowingAReferenceTarget_WhenAResourceReferencesATypeOnlyTheCoreAllows_ThenItIsInvalidUnderTheProfile()
    {
        using var folder = new TestFolder();
        folder.Write("sd.json", """
            {"resourceType":"StructureDefinition","id":"gp-org","url":"http://example.org/fhir/StructureDefinition/gp-org","version":"1.0.0","name":"GpOrg","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Patient","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Patient","derivation":"constraint",
             "differential":{"element":[{"id":"Patient.generalPractitioner","path":"Patient.generalPractitioner","type":[{"code":"Reference","targetProfile":["http://hl7.org/fhir/StructureDefinition/Organization"]}]}]}}
            """);
        folder.Write("r.ndjson", """
            {"resourceType":"Patient","id":"bad","meta":{"profile":["http://example.org/fhir/StructureDefinition/gp-org"]},"generalPractitioner":[{"reference":"Practitioner/x"}]}
            {"resourceType":"Patient","id":"good","meta":{"profile":["http://example.org/fhir/StructureDefinition/gp-org"]},"generalPractitioner":[{"reference":"Organization/o1"}]}
            """);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(ResourceType: "Patient", Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(1);
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == Rule)!;
        ((string)group["profile"]!).ShouldBe("http://example.org/fhir/StructureDefinition/gp-org");
        Strings(group["samples"]!).ShouldBe(["Patient/bad"]);
    }

    // Regression: at 29aa44d only the core binding (example, for Observation.code) was read, so pb1 had no binding-not-checked.
    [Fact]
    public async Task GivenAFolderProfileMakingABindingRequired_WhenATextOnlyConceptIsValidated_ThenItsBindingIsNotCheckedAndNamesTheProfileValueSet()
    {
        using var folder = new TestFolder();
        folder.Write("sd.json", """
            {"resourceType":"StructureDefinition","id":"obsbind","url":"http://example.org/fhir/StructureDefinition/obsbind","version":"1.0.0","name":"ObsBind","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Observation","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Observation","derivation":"constraint",
             "differential":{"element":[{"id":"Observation.code","path":"Observation.code","binding":{"strength":"required","valueSet":"http://hl7.org/fhir/ValueSet/observation-codes"}}]}}
            """);
        folder.Write("r.ndjson", $$"""{"resourceType":"Observation","id":"pb1","meta":{"profile":["http://example.org/fhir/StructureDefinition/obsbind"]},"status":"final","code":{"text":"anything at all"},{{Narrative}}}""");
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(ResourceType: "Observation", Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["unknown"]!).ShouldBe(1);
        JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == "binding-not-checked" && (string)item["path"]! == "Observation.code")!;
        ((string)group["valueSet"]!).ShouldBe("http://hl7.org/fhir/ValueSet/observation-codes");
        ((string)group["profile"]!).ShouldBe("http://example.org/fhir/StructureDefinition/obsbind");
    }

    // Regression: at 29aa44d the profile's dom-6 got the core narrative description, which is not what that profile's key means.
    [Fact]
    public async Task GivenAProfileInvariantReusingACoreKey_WhenSummarized_ThenOnlyTheCoreOneGetsTheCoreDescription()
    {
        using var folder = new TestFolder();
        folder.Write("sd.json", """
            {"resourceType":"StructureDefinition","id":"key","url":"http://example.org/fhir/StructureDefinition/key","version":"1.0.0","name":"Key","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Patient","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Patient","derivation":"constraint",
             "differential":{"element":[{"id":"Patient","path":"Patient","constraint":[{"key":"dom-6","severity":"error","human":"x","expression":"false","source":"http://example.org/fhir/StructureDefinition/key"}]}]}}
            """);
        folder.Write("r.ndjson", """{"resourceType":"Patient","id":"p","meta":{"profile":["http://example.org/fhir/StructureDefinition/key"]}}""");
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(ResourceType: "Patient", Depth: "full")));

        JsonNode[] dom6 = [.. Groups(result).Where(item => (string?)item!["constraint"] == "dom-6").Select(item => item!)];
        JsonNode profile = dom6.Single(item => (string)item["profile"]! == "http://example.org/fhir/StructureDefinition/key");
        ((string)profile["description"]!).ShouldNotContain("narrative", Case.Insensitive);
        JsonNode core = dom6.Single(item => (string)item["profile"]! == "http://hl7.org/fhir/StructureDefinition/Patient");
        ((string)core["description"]!).ShouldContain("narrative");
    }

    private static JsonNode Json<TFacts>(Contracts.ToolEnvelope<TFacts> envelope) => TerminologyFixture.Json(envelope);

    private static JsonArray Groups(JsonNode result) => result["facts"]!["groups"]!.AsArray();

    private static string[] Rules(JsonNode result) => [.. Groups(result).Select(group => (string)group!["rule"]!)];

    private static string[] Strings(JsonNode node) => [.. node.AsArray().Select(item => (string)item!)];
}
