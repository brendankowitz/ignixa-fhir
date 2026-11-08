// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests.Infrastructure;

/// <summary>
/// A layered provider answers where each loaded extension may be used and whether it is a modifier, through the optional
/// <see cref="IExtensionDefinitionInfo"/> lookup (Task 9b fix round 1, F2). These do not compile at b0f34da, where the lookup
/// does not exist.
/// </summary>
public class ExtensionDefinitionInfoTests
{
    [Fact]
    public void GivenAnR4ExtensionDefinition_WhenLookedUpByUrl_ThenItsContextsAndModifierStatusAreRead()
    {
        var provider = Layered(Definition(
            "ext-a",
            """[{"type":"element","expression":"Patient"},{"type":"extension","expression":"http://example.org/parent"},{"type":"fhirpath","expression":"Patient.name"}]""",
            isModifier: true));

        var info = ((IExtensionDefinitionInfo)provider).GetExtensionDefinition(Url("ext-a"));

        info.ShouldNotBeNull();
        info.IsModifier.ShouldBeTrue();
        info.Contexts.ShouldBe(
        [
            new ExtensionContext(ExtensionContextType.Element, "Patient"),
            new ExtensionContext(ExtensionContextType.Extension, "http://example.org/parent"),
            new ExtensionContext(ExtensionContextType.FhirPath, "Patient.name"),
        ]);
    }

    [Fact]
    public void GivenAnStu3ExtensionDefinition_WhenLookedUpByUrl_ThenItsContextTypeAppliesToEachContext()
    {
        const string definition = """
            {"resourceType":"StructureDefinition","id":"ext-s","url":"http://example.org/fhir/StructureDefinition/ext-s","version":"1.0.0","name":"Ext","status":"active","fhirVersion":"3.0.2",
             "kind":"complex-type","abstract":false,"contextType":"resource","context":["Patient","Observation"],"type":"Extension","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Extension","derivation":"constraint",
             "differential":{"element":[{"id":"Extension","path":"Extension"},{"id":"Extension.value[x]","path":"Extension.value[x]","type":[{"code":"code"}]}]}}
            """;

        var provider = Layered(definition, new STU3CoreSchemaProvider(), "3.0.2");

        var info = ((IExtensionDefinitionInfo)provider).GetExtensionDefinition("http://example.org/fhir/StructureDefinition/ext-s");

        info.ShouldNotBeNull();
        info.Contexts.ShouldBe(
        [
            new ExtensionContext(ExtensionContextType.Element, "Patient"),
            new ExtensionContext(ExtensionContextType.Element, "Observation"),
        ]);
    }

    [Fact]
    public void GivenAnExtensionDefinitionWithAVersion_WhenLookedUpByVersionedUrl_ThenItIsFound()
    {
        var provider = Layered(Definition("ext-b", """[{"type":"element","expression":"Element"}]"""));

        var lookup = (IExtensionDefinitionInfo)provider;

        lookup.GetExtensionDefinition($"{Url("ext-b")}|1.0.0").ShouldNotBeNull();
        lookup.GetExtensionDefinition(Url("ext-b"))!.IsModifier.ShouldBeFalse();
        lookup.GetExtensionDefinition(Url("unknown")).ShouldBeNull();
    }

    [Fact]
    public void GivenAnUnrecognisedContextType_WhenRead_ThenItIsTreatedAsFhirPathSoItIsNeverGuessed()
    {
        var provider = Layered(Definition("ext-c", """[{"type":"somethingelse","expression":"Patient"}]"""));

        var info = ((IExtensionDefinitionInfo)provider).GetExtensionDefinition(Url("ext-c"));

        info!.Contexts.ShouldBe([new ExtensionContext(ExtensionContextType.FhirPath, "Patient")]);
    }

    [Fact]
    public void GivenAProfileThatIsNotAnExtension_WhenLookedUp_ThenItIsNotAnExtensionDefinition()
    {
        const string profile = """
            {"resourceType":"StructureDefinition","id":"pat","url":"http://example.org/fhir/StructureDefinition/pat","version":"1.0.0","name":"Pat","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Patient","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Patient","derivation":"constraint",
             "differential":{"element":[{"id":"Patient","path":"Patient"}]}}
            """;

        var provider = Layered(profile);

        ((IExtensionDefinitionInfo)provider).GetExtensionDefinition("http://example.org/fhir/StructureDefinition/pat").ShouldBeNull();
    }

    private static ProfileLayeredSchemaProvider Layered(string definition) => Layered(definition, new R4CoreSchemaProvider(), "4.0.1");

    private static ProfileLayeredSchemaProvider Layered(string definition, Ignixa.Abstractions.IFhirSchemaProvider baseProvider, string fhirVersion)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(definition)!.AsObject();
        return new ProfileLayeredSchemaProvider(baseProvider,
        [
            new ExtractedResource
            {
                ResourceType = "StructureDefinition",
                Canonical = (string)root["url"]!,
                Version = (string?)root["version"],
                ResourceId = (string)root["id"]!,
                ResourceJson = definition,
                FhirVersion = fhirVersion,
            },
        ]);
    }

    private static string Url(string id) => $"http://example.org/fhir/StructureDefinition/{id}";

    private static string Definition(string id, string contexts, bool isModifier = false) => """
        {"resourceType":"StructureDefinition","id":"@ID","url":"@URL","version":"1.0.0","name":"Ext","status":"active","fhirVersion":"4.0.1",
         "kind":"complex-type","abstract":false,"context":@CONTEXTS,"type":"Extension","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Extension","derivation":"constraint",
         "differential":{"element":[{"id":"Extension","path":"Extension"@MODIFIER},{"id":"Extension.value[x]","path":"Extension.value[x]","type":[{"code":"code"}]}]}}
        """
        .Replace("@ID", id, StringComparison.Ordinal)
        .Replace("@URL", Url(id), StringComparison.Ordinal)
        .Replace("@CONTEXTS", contexts, StringComparison.Ordinal)
        .Replace("@MODIFIER", isModifier ? ",\"isModifier\":true,\"isModifierReason\":\"Changes the meaning\"" : string.Empty, StringComparison.Ordinal);
}
