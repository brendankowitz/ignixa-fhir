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
/// Task 9 fix round 3: how a reference's type is told and which profile rules apply. Tests marked regression fail at
/// a5cace5: there <c>Reference.type</c> and the reference were taken as a union (a disagreement passed, or was reported
/// against the wrong type), a <c>#id</c> and a foreign server's id that is not a FHIR id were undeterminable, a display-only
/// reference counted as undeterminable, and a profile with only a differential lost the rules of the profile it derives from.
/// The tests read the envelope as JSON, so they compile there.
/// </summary>
public class ValidateReferenceTypeTests
{
    private const string TargetType = "reference-target-type";

    private const string Mismatch = "reference-type-mismatch";

    // Regression: at a5cace5 Patient/p1 typed Group passed (both are allowed subjects), and typed Organization was reported
    // as a reference to an Organization.
    [Theory]
    [InlineData("Group")]
    [InlineData("Organization")]
    public async Task GivenAReferenceWhoseTypeDisagreesWithIt_WhenValidated_ThenItIsATypeMismatchCheckedAgainstTheReferencesOwnType(string declared)
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", $$$"""{"resourceType":"Observation","id":"o","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/p1","type":"{{{declared}}}"}}""");
        using var store = new TestFolder();

        JsonNode result = await ValidateAsync(folder, store);

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(1);
        JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == Mismatch)!;
        ((string)group["path"]!).ShouldBe("Observation.subject");
        ((string)group["severity"]!).ShouldBe("error");
        Rules(result).ShouldNotContain(TargetType);
    }

    // Guard: a type that agrees with the reference, or a type alone, is checked as before.
    [Fact]
    public async Task GivenAReferenceWithAnAgreeingTypeOrATypeAlone_WhenValidated_ThenOnlyADisallowedTypeIsReported()
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", """
            {"resourceType":"Patient","id":"agree","managingOrganization":{"reference":"Organization/o1","type":"Organization"}}
            {"resourceType":"Patient","id":"alone","managingOrganization":{"type":"Patient","identifier":{"system":"http://example.org/p","value":"1"}}}
            """);
        using var store = new TestFolder();

        JsonNode result = await ValidateAsync(folder, store);

        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        Rules(result).ShouldNotContain(Mismatch);
        Strings(Groups(result).Single(item => (string)item!["rule"]! == TargetType)!["samples"]!).ShouldBe(["Patient/alone"]);
    }

    // Regression: at a5cace5 a #id reference was undeterminable, so a contained Organization as subject passed.
    [Fact]
    public async Task GivenAContainedReference_WhenValidated_ThenTheContainedResourcesTypeIsChecked()
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", """
            {"resourceType":"Observation","id":"bad","status":"final","code":{"text":"x"},"contained":[{"resourceType":"Organization","id":"c1","name":"x"}],"subject":{"reference":"#c1"}}
            {"resourceType":"Patient","id":"good","contained":[{"resourceType":"Organization","id":"c1","name":"x"}],"managingOrganization":{"reference":"#c1"}}
            """);
        using var store = new TestFolder();

        JsonNode result = await ValidateAsync(folder, store);

        Strings(Groups(result).Single(item => (string)item!["rule"]! == TargetType)!["samples"]!).ShouldBe(["Observation/bad"]);
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        Strings(result["facts"]!["against"]!["checksNotRun"]!).ShouldNotContain(item => item.StartsWith(TargetType, StringComparison.Ordinal));
    }

    // Regression: at a5cace5 a foreign server's id that is not a FHIR id made the reference undeterminable.
    [Fact]
    public async Task GivenAbsoluteReferencesToAForeignServerWithNonFhirIds_WhenValidated_ThenTheirTypesAreChecked()
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", """
            {"resourceType":"Patient","id":"bad","managingOrganization":{"reference":"https://other.example/fhir/Patient/abc_def"}}
            {"resourceType":"Patient","id":"good","managingOrganization":{"reference":"https://other.example/fhir/Organization/abc_def/_history/2"}}
            """);
        using var store = new TestFolder();

        JsonNode result = await ValidateAsync(folder, store);

        Strings(Groups(result).Single(item => (string)item!["rule"]! == TargetType)!["samples"]!).ShouldBe(["Patient/bad"]);
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        Strings(result["facts"]!["against"]!["checksNotRun"]!).ShouldNotContain(item => item.StartsWith(TargetType, StringComparison.Ordinal));
    }

    // Regression: at a5cace5 a display-only reference was counted as undeterminable.
    [Fact]
    public async Task GivenADisplayOnlyReference_WhenValidated_ThenItIsCountedAsDisplayOnlyNotUndeterminable()
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", """
            {"resourceType":"Patient","id":"d","managingOrganization":{"display":"Acme"}}
            {"resourceType":"Patient","id":"u","managingOrganization":{"reference":"urn:uuid:9b1f6c1e-0000-4000-8000-000000000002"}}
            """);
        using var store = new TestFolder();

        JsonNode result = await ValidateAsync(folder, store);

        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(2);
        string[] notRun = Strings(result["facts"]!["against"]!["checksNotRun"]!);
        notRun.ShouldContain("reference-target-type (display-only: 1)");
        notRun.ShouldContain("reference-target-type (undeterminable: 1)");
    }

    // Regression: at a5cace5 gp-child (an empty differential over gp-myorg) had no target rule, so a Practitioner passed.
    [Fact]
    public async Task GivenADifferentialOnlyProfileOverAProfileNarrowingATarget_WhenValidated_ThenTheInheritedTargetIsChecked()
    {
        using var folder = new TestFolder();
        folder.Write("sd-my-org.json", Profile("my-org", "Organization", "http://hl7.org/fhir/StructureDefinition/Organization", "[]"));
        folder.Write("sd-gp-myorg.json", Profile("gp-myorg", "Patient", "http://hl7.org/fhir/StructureDefinition/Patient", """[{"id":"Patient.generalPractitioner","path":"Patient.generalPractitioner","type":[{"code":"Reference","targetProfile":["http://example.org/fhir/StructureDefinition/my-org"]}]}]"""));
        folder.Write("sd-gp-child.json", Profile("gp-child", "Patient", "http://example.org/fhir/StructureDefinition/gp-myorg", "[]"));
        folder.Write("r.ndjson", """
            {"resourceType":"Patient","id":"c1","meta":{"profile":["http://example.org/fhir/StructureDefinition/gp-child"]},"generalPractitioner":[{"reference":"Practitioner/o"}]}
            {"resourceType":"Patient","id":"c2","meta":{"profile":["http://example.org/fhir/StructureDefinition/gp-child"]},"generalPractitioner":[{"reference":"Organization/o"}]}
            """);
        using var store = new TestFolder();

        JsonNode result = await ValidateAsync(folder, store, "Patient");

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(1);
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == TargetType)!;
        ((string)group["profile"]!).ShouldBe("http://example.org/fhir/StructureDefinition/gp-child");
        Strings(group["samples"]!).ShouldBe(["Patient/c1"]);
    }

    // Regression: at a5cace5 obs-child (an empty differential over obs-parent) had no binding rule, so a text-only code passed unchecked.
    [Fact]
    public async Task GivenADifferentialOnlyProfileOverAProfileRequiringABinding_WhenATextOnlyConceptIsValidated_ThenTheInheritedBindingIsNotChecked()
    {
        using var folder = new TestFolder();
        folder.Write("sd-parent.json", Profile("obs-parent", "Observation", "http://hl7.org/fhir/StructureDefinition/Observation", """[{"id":"Observation.code","path":"Observation.code","binding":{"strength":"required","valueSet":"http://hl7.org/fhir/ValueSet/observation-codes"}}]"""));
        folder.Write("sd-child.json", Profile("obs-child", "Observation", "http://example.org/fhir/StructureDefinition/obs-parent", "[]"));
        folder.Write("r.ndjson", """{"resourceType":"Observation","id":"t","meta":{"profile":["http://example.org/fhir/StructureDefinition/obs-child"]},"status":"final","code":{"text":"anything"}}""");
        using var store = new TestFolder();

        JsonNode result = await ValidateAsync(folder, store, "Observation");

        JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == "binding-not-checked" && (string)item["path"]! == "Observation.code")!;
        ((string)group["valueSet"]!).ShouldBe("http://hl7.org/fhir/ValueSet/observation-codes");
        ((string)group["profile"]!).ShouldBe("http://example.org/fhir/StructureDefinition/obs-child");
    }

    // Guard: a profile whose base is not loaded cannot have a snapshot generated, so it is never applied with only its own
    // rules (as if unconstrained above it): its resources are profile-not-loaded and unknown.
    [Fact]
    public async Task GivenAProfileDerivingFromOneNotLoaded_WhenValidated_ThenItIsProfileNotLoadedAndTheVerdictUnknown()
    {
        using var folder = new TestFolder();
        folder.Write("sd-orphan.json", Profile("orphan", "Patient", "http://example.org/fhir/StructureDefinition/not-loaded-base", "[]"));
        folder.Write("r.ndjson", """{"resourceType":"Patient","id":"o","meta":{"profile":["http://example.org/fhir/StructureDefinition/orphan"]},"generalPractitioner":[{"reference":"Practitioner/x"}]}""");
        using var store = new TestFolder();

        JsonNode result = await ValidateAsync(folder, store, "Patient");

        ((int)result["facts"]!["verdicts"]!["unknown"]!).ShouldBe(1);
        JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == "profile-not-loaded")!;
        ((string)group["profile"]!).ShouldBe("http://example.org/fhir/StructureDefinition/orphan");
        result["issues"]!.AsArray().ShouldContain(issue => (string)issue!["code"]! == "profile-unusable");
    }

    private static string Profile(string id, string type, string baseDefinition, string differential) => $$$"""
        {"resourceType":"StructureDefinition","id":"{{{id}}}","url":"http://example.org/fhir/StructureDefinition/{{{id}}}","version":"1.0.0","name":"{{{id.Replace("-", string.Empty, StringComparison.Ordinal)}}}","status":"active","fhirVersion":"4.0.1",
         "kind":"resource","abstract":false,"type":"{{{type}}}","baseDefinition":"{{{baseDefinition}}}","derivation":"constraint","differential":{"element":{{{differential}}}}}
        """;

    private static async Task<JsonNode> ValidateAsync(TestFolder folder, TestFolder store, string? type = null) =>
        TerminologyFixture.Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(ResourceType: type, Depth: "full")));

    private static JsonArray Groups(JsonNode result) => result["facts"]!["groups"]!.AsArray();

    private static string[] Rules(JsonNode result) => [.. Groups(result).Select(group => (string)group!["rule"]!)];

    private static string[] Strings(JsonNode node) => [.. node.AsArray().Select(item => (string)item!)];
}
