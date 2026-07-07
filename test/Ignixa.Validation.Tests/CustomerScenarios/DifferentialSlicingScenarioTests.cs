// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Infrastructure.Snapshot;
using Ignixa.PackageManagement.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// Deterministic (no-network) end-to-end proof that a <b>differential-only</b> sliced profile is
/// validated correctly: the M1/M2 <see cref="SnapshotGenerator"/> generates the snapshot (carrying
/// the slicing metadata and slice members), <see cref="StructureDefinitionTypeAdapter"/> surfaces
/// them, <see cref="StructureDefinitionSchemaBuilder"/> wires a <see cref="SlicingCheck"/>, and the
/// check enforces per-slice cardinality and closed-slicing rules against a Patient instance.
/// </summary>
public sealed class DifferentialSlicingScenarioTests(ITestOutputHelper output)
{
    private const string ProfileUrl = "http://example.org/StructureDefinition/sliced-patient";
    private const string RaceExtUrl = "http://example.org/StructureDefinition/race";
    private const string BirthsexExtUrl = "http://example.org/StructureDefinition/birthsex";

    private readonly ITestOutputHelper _output = output;
    private readonly R4CoreSchemaProvider _base = new();

    private ValidationSchema BuildSlicedPatientSchema(string rules)
    {
        var profileJson = """
        {
          "resourceType": "StructureDefinition",
          "url": "__PROFILE__",
          "type": "Patient",
          "kind": "resource",
          "baseDefinition": "http://hl7.org/fhir/StructureDefinition/Patient",
          "differential": {
            "element": [
              {"id":"Patient.extension","path":"Patient.extension","slicing":{"discriminator":[{"type":"value","path":"url"}],"rules":"__RULES__"}},
              {"id":"Patient.extension:race","path":"Patient.extension","sliceName":"race","min":0,"max":"1","type":[{"code":"Extension","profile":["__RACE__"]}]},
              {"id":"Patient.extension:birthsex","path":"Patient.extension","sliceName":"birthsex","min":0,"max":"1","type":[{"code":"Extension","profile":["__BIRTHSEX__"]}]}
            ]
          }
        }
        """
            .Replace("__PROFILE__", ProfileUrl, StringComparison.Ordinal)
            .Replace("__RULES__", rules, StringComparison.Ordinal)
            .Replace("__RACE__", RaceExtUrl, StringComparison.Ordinal)
            .Replace("__BIRTHSEX__", BirthsexExtUrl, StringComparison.Ordinal);

        var profile = JsonNode.Parse(profileJson) as JsonObject;

        var resolver = new PackageSnapshotBaseResolver(Array.Empty<ExtractedResource>(), _base);
        var generated = new SnapshotGenerator().GenerateSnapshotElements(profile!, resolver);
        generated.ShouldNotBeNull("snapshot generation must succeed for a differential-only Patient profile");

        profile!["snapshot"] = new JsonObject { ["element"] = generated };

        var type = new StructureDefinitionTypeAdapter().Adapt(profile.ToJsonString(), "4.0.1");
        type.ShouldNotBeNull();

        return new StructureDefinitionSchemaBuilder().BuildSchema(type!, _base);
    }

    private IElement Patient(string extensionsJson)
    {
        var json = JsonNode.Parse("""{"resourceType":"Patient","id":"p1","extension":__EXT__}"""
            .Replace("__EXT__", extensionsJson, StringComparison.Ordinal));
        return JsonNodeSourceNode.Create(json).ToElement(_base);
    }

    private static IReadOnlyList<ValidationIssue> SlicingIssues(ValidationResult result)
        => result.Issues.Where(i => i.Code.StartsWith("slicing", StringComparison.Ordinal)).ToList();

    private void Dump(ValidationResult result, string label)
    {
        _output.WriteLine($"--- {label} ---");
        foreach (var i in result.Issues)
        {
            _output.WriteLine($"  [{i.Severity}] {i.Code} @ {i.Path}: {i.Message}");
        }
    }

    [Fact]
    public void GivenDifferentialOnlyProfile_WhenSchemaBuilt_ThenASlicingCheckIsWired()
    {
        var schema = BuildSlicedPatientSchema("open");

        schema.Checks.OfType<SlicingCheck>()
            .ShouldContain(c => c.SlicedName == "extension");
    }

    [Fact]
    public void GivenConformantPatient_WhenValidatingAtFullDepth_ThenNoSlicingErrors()
    {
        var schema = BuildSlicedPatientSchema("open");
        var patient = Patient($$"""
        [
          {"url":"{{RaceExtUrl}}","valueString":"Asian"},
          {"url":"{{BirthsexExtUrl}}","valueCode":"F"}
        ]
        """);

        var result = schema.Validate(patient, new ValidationSettings { Depth = ValidationDepth.Full }, new ValidationState());

        Dump(result, "conformant");
        SlicingIssues(result).ShouldBeEmpty();
    }

    [Fact]
    public void GivenDuplicateSingleCardinalitySlice_WhenValidatingAtFullDepth_ThenRejectedWithSliceSpecificMessage()
    {
        var schema = BuildSlicedPatientSchema("open");
        var patient = Patient($$"""
        [
          {"url":"{{RaceExtUrl}}","valueString":"Asian"},
          {"url":"{{RaceExtUrl}}","valueString":"White"}
        ]
        """);

        var result = schema.Validate(patient, new ValidationSettings { Depth = ValidationDepth.Full }, new ValidationState());

        Dump(result, "duplicate race");
        var issue = SlicingIssues(result).ShouldHaveSingleItem();
        issue.Severity.ShouldBe(IssueSeverity.Error);
        issue.Code.ShouldBe("slicing-cardinality");
        issue.Message.ShouldContain("race");
    }

    [Fact]
    public void GivenUnknownExtensionUnderClosedSlicing_WhenValidatingAtFullDepth_ThenRejected()
    {
        var schema = BuildSlicedPatientSchema("closed");
        var patient = Patient($$"""
        [
          {"url":"{{RaceExtUrl}}","valueString":"Asian"},
          {"url":"http://example.org/StructureDefinition/unknown","valueString":"?"}
        ]
        """);

        var result = schema.Validate(patient, new ValidationSettings { Depth = ValidationDepth.Full }, new ValidationState());

        Dump(result, "closed unknown");
        SlicingIssues(result).ShouldContain(i => i.Code == "slicing-unmatched" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenViolation_WhenValidatingAtSpecDepth_ThenSlicingNotEnforced()
    {
        var schema = BuildSlicedPatientSchema("closed");
        var patient = Patient($$"""
        [
          {"url":"{{RaceExtUrl}}","valueString":"Asian"},
          {"url":"{{RaceExtUrl}}","valueString":"White"}
        ]
        """);

        var result = schema.Validate(patient, new ValidationSettings { Depth = ValidationDepth.Spec }, new ValidationState());

        SlicingIssues(result).ShouldBeEmpty("slicing is a Full-tier check and must not run at Spec depth");
    }
}
