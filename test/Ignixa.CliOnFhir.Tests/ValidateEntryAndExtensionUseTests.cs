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
/// Task 9b fix round 1, on the reviewer's fixtures: a Bundle stored as one resource has its entries validated (F1), an
/// extension's use is checked against its definition (F2), and the insides of CodeableConcept, Coding and Reference are
/// validated (F4). Regressions fail at b0f34da, where each of these read 'valid'.
/// </summary>
public class ValidateEntryAndExtensionUseTests
{
    private const string PatientExtension = """
        {"resourceType":"StructureDefinition","id":"ext-g","url":"http://example.org/fhir/StructureDefinition/ext-g","version":"1.0.0","name":"ExtG","status":"active","fhirVersion":"4.0.1",
         "kind":"complex-type","abstract":false,"context":[{"type":"element","expression":"Patient"}],"type":"Extension","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Extension","derivation":"constraint",
         "differential":{"element":[{"id":"Extension","path":"Extension","max":"1"},{"id":"Extension.extension","path":"Extension.extension","max":"0"},{"id":"Extension.url","path":"Extension.url","fixedUri":"http://example.org/fhir/StructureDefinition/ext-g"},{"id":"Extension.value[x]","path":"Extension.value[x]","min":1,"type":[{"code":"code"}]}]}}
        """;

    private const string FhirPathExtension = """
        {"resourceType":"StructureDefinition","id":"ext-fp","url":"http://example.org/fhir/StructureDefinition/ext-fp","version":"1.0.0","name":"ExtFp","status":"active","fhirVersion":"4.0.1",
         "kind":"complex-type","abstract":false,"context":[{"type":"fhirpath","expression":"Patient.name.where(use = 'official')"}],"type":"Extension","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Extension","derivation":"constraint",
         "differential":{"element":[{"id":"Extension","path":"Extension","max":"1"},{"id":"Extension.extension","path":"Extension.extension","max":"0"},{"id":"Extension.url","path":"Extension.url","fixedUri":"http://example.org/fhir/StructureDefinition/ext-fp"},{"id":"Extension.value[x]","path":"Extension.value[x]","min":1,"type":[{"code":"code"}]}]}}
        """;

    [Fact]
    public async Task GivenABundleOnAnNdjsonLineWithAnInvalidEntry_WhenValidated_ThenItIsInvalidAtTheEntrysPath()
    {
        using var folder = new TestFolder();
        folder.Write("b.ndjson", """
            {"resourceType":"Bundle","id":"bb","type":"collection","entry":[{"fullUrl":"http://ex.org/fhir/Patient/p1","resource":{"resourceType":"Patient","id":"p1"}},{"fullUrl":"http://ex.org/fhir/Observation/o9","resource":{"resourceType":"Observation","id":"o9","status":"final","zzz":1,"code":{"text":"x"},"subject":{"reference":"http://ex.org/fhir/Patient/p1"}}}]}
            """);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(ResourceType: "Bundle", Depth: "spec")));

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(1, result.ToJsonString());
        Groups(result).ShouldContain(group => (string)group!["severity"]! == "error" && (string)group!["rule"]! == "unknown-property" && ((string)group!["path"]!).StartsWith("Bundle.entry.resource.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenExtensionsUsedOutsideTheirContexts_WhenValidatedAtFull_ThenTheyAreInvalidWithAToolOwnedDescription()
    {
        using var folder = new TestFolder();
        folder.Write("sd.json", PatientExtension);
        folder.Write("r.ndjson", """
            {"resourceType":"Observation","id":"wrongctx","status":"final","code":{"text":"x"},"extension":[{"url":"http://example.org/fhir/StructureDefinition/ext-g","valueCode":"male"}]}
            {"resourceType":"Patient","id":"asmod","modifierExtension":[{"url":"http://example.org/fhir/StructureDefinition/ext-g","valueCode":"male"}]}
            {"resourceType":"Patient","id":"good","extension":[{"url":"http://example.org/fhir/StructureDefinition/ext-g","valueCode":"male"}]}
            """);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(References: ["Observation/wrongctx", "Patient/asmod", "Patient/good"], Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(2, result.ToJsonString());
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(1, result.ToJsonString());
        foreach (var rule in new[] { "extension-context", "extension-modifier-mismatch" })
        {
            JsonNode group = Groups(result).Single(item => (string)item!["rule"]! == rule)!;
            ((string?)group["description"]).ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task GivenAnExtensionWithAFhirPathContext_WhenValidatedAtFull_ThenTheVerdictIsUnknown()
    {
        using var folder = new TestFolder();
        folder.Write("sd.json", FhirPathExtension);
        folder.Write("r.ndjson", """
            {"resourceType":"Patient","id":"p","name":[{"family":"x","extension":[{"url":"http://example.org/fhir/StructureDefinition/ext-fp","valueCode":"male"}]}]}
            """);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(References: ["Patient/p"], Depth: "full")));

        ((int)result["facts"]!["verdicts"]!["unknown"]!).ShouldBe(1, result.ToJsonString());
        Rules(result).ShouldContain("extension-context-unchecked");
    }

    [Fact]
    public async Task GivenBrokenInsidesOfCodeableConceptCodingAndReference_WhenValidatedAtSpec_ThenTheResourceIsInvalid()
    {
        using var folder = new TestFolder();
        folder.Write("r.ndjson", """
            {"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"1-8","version":5}],"foo":"x"},"subject":{"reference":"Patient/1","actor":{"display":"y"}}}
            """);
        using var store = new TestFolder();

        JsonNode result = Json(await ResourceValidator.ValidateAsync(WithTerminology(folder, store), new ValidateRequest(Depth: "spec")));

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(1);
        Rules(result).ShouldContain("unknown-property");
    }

    private static JsonNode Json<TFacts>(Contracts.ToolEnvelope<TFacts> envelope) => TerminologyFixture.Json(envelope);

    private static JsonArray Groups(JsonNode result) => result["facts"]!["groups"]!.AsArray();

    private static string[] Rules(JsonNode result) => [.. Groups(result).Select(group => (string)group!["rule"]!)];
}
