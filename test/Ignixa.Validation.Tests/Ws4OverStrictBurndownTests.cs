// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Covers the WS4 over-strict burndown fixes: cases the Java reference accepts that Ignixa previously
/// rejected. Each test pins one root-cause fix — ele-1 on the resource root, eld-3 hoisting onto the
/// wrong altitude, the Element.id vs Resource.id distinction, the 1 MB string cap, and Example-strength
/// binding severity — while a paired assertion guards that the legitimate catch is preserved.
/// </summary>
public sealed class Ws4OverStrictBurndownTests
{
    private static readonly ISchema Schema = new R4CoreSchemaProvider();

    private static readonly IValidationSchemaResolver Resolver =
        new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(Schema));

    private static ValidationResult ValidateFull(string json)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        var schema = Resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{element.InstanceType}")!;
        var state = new ValidationState().EnterRootResource(element);
        return schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Full }, state);
    }

    [Fact]
    public void GivenEmptyParametersResource_WhenValidating_ThenEle1DoesNotFireOnRoot()
    {
        // Arrange / Act
        var result = ValidateFull("""{ "resourceType": "Parameters" }""");

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "ele-1");
    }

    [Fact]
    public void GivenPatientWithOnlyId_WhenValidating_ThenEle1DoesNotFireOnRoot()
    {
        // Arrange / Act
        var result = ValidateFull("""{ "resourceType": "Patient", "id": "matchetype" }""");

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "ele-1");
    }

    [Fact]
    public void GivenEmptyComplexDatatype_WhenValidating_ThenEle1StillFiresOnTheNestedElement()
    {
        // Arrange - an empty CodeableConcept has neither value nor children; ele-1 must still catch it
        // on the nested element even though the resource root is exempt.
        var result = ValidateFull("""
        {
            "resourceType": "Observation",
            "status": "final",
            "code": {}
        }
        """);

        // Assert
        result.Issues.ShouldContain(i => i.Code == "ele-1");
    }

    [Fact]
    public void GivenStructureDefinitionWhoseDifferentialOmitsMax_WhenValidating_ThenEld3DoesNotFire()
    {
        // Arrange - a differential element carrying no max value. eld-3 (Max SHALL be a number or "*")
        // must not fire: an absent max satisfies the constraint, and it is scoped to ElementDefinition.max,
        // not the whole ElementDefinition.
        var result = ValidateFull("""
        {
            "resourceType": "StructureDefinition",
            "url": "http://example.org/fhir/StructureDefinition/test",
            "name": "Test",
            "status": "draft",
            "kind": "resource",
            "abstract": false,
            "type": "Patient",
            "baseDefinition": "http://hl7.org/fhir/StructureDefinition/Patient",
            "derivation": "constraint",
            "differential": {
                "element": [
                    { "id": "Patient.name", "path": "Patient.name", "short": "the name" }
                ]
            }
        }
        """);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "eld-3");
    }

    [Fact]
    public void GivenElementIdWithSpecialCharacters_WhenValidating_ThenNoIdFormatError()
    {
        // Arrange - Location.position.id is Element.id (a plain System.String), which accepts values
        // the FHIR id datatype would reject. The reference validator does not flag "/foobar==".
        var result = ValidateFull("""
        {
            "resourceType": "Location",
            "id": "foo-bar",
            "name": "A Location",
            "position": { "id": "/foobar==", "longitude": 3.24, "latitude": 3.24 }
        }
        """);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "type-1");
    }

    [Fact]
    public void GivenResourceIdViolatingIdRules_WhenValidating_ThenTypeErrorStillFires()
    {
        // Arrange - Resource.id IS the FHIR id datatype; a slash is not permitted.
        var result = ValidateFull("""{ "resourceType": "Patient", "id": "bad/id" }""");

        // Assert
        result.Issues.ShouldContain(i => i.Code == "type-1");
    }

    [Fact]
    public void GivenStringValueExceedingOneMegabyte_WhenValidating_ThenTypeErrorFires()
    {
        // Arrange - a >1 MB Element.id. FHIR caps every string-valued primitive at 1,048,576 bytes.
        var oversized = new string('a', (1024 * 1024) + 1);
        var result = ValidateFull($$"""
        {
            "resourceType": "Location",
            "id": "foo-bar",
            "position": { "id": "{{oversized}}", "longitude": 3.24, "latitude": 3.24 }
        }
        """);

        // Assert
        result.Issues.ShouldContain(i => i.Code == "type-1" && i.Message.Contains("1 MB"));
    }

    [Fact]
    public void GivenExampleBindingWithUnmatchedCode_WhenValidating_ThenMissIsWarningNotError()
    {
        // Arrange - an Example-strength binding never hard-errors on a code outside its value set.
        var terminology = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);
        var json = JsonNode.Parse("""
        {
            "resourceType": "Observation",
            "code": { "coding": [{ "system": "http://hl7.org/fhir/administrative-gender", "code": "not-a-real-code" }] }
        }
        """);
        var check = new BindingCheck(
            "code",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Example",
            terminology);

        // Act
        var result = check.Validate(
            JsonNodeSourceNode.Create(json).ToElement(TestSchemaProvider.GetR4Schema()),
            new ValidationSettings { Depth = ValidationDepth.Full },
            new ValidationState());

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldNotContain(i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void GivenRequiredBindingWithUnmatchedCode_WhenValidating_ThenMissIsError()
    {
        // Arrange - the Example downgrade must not weaken Required bindings.
        var terminology = new InMemoryTerminologyService(new R4CoreSchemaProvider().ValueSetProvider);
        var json = JsonNode.Parse("""
        {
            "resourceType": "Observation",
            "code": { "coding": [{ "system": "http://hl7.org/fhir/administrative-gender", "code": "not-a-real-code" }] }
        }
        """);
        var check = new BindingCheck(
            "code",
            "http://hl7.org/fhir/ValueSet/administrative-gender",
            "Required",
            terminology);

        // Act
        var result = check.Validate(
            JsonNodeSourceNode.Create(json).ToElement(TestSchemaProvider.GetR4Schema()),
            new ValidationSettings { Depth = ValidationDepth.Full },
            new ValidationState());

        // Assert
        result.Issues.ShouldContain(i => i.Code == "code-invalid" && i.Severity == IssueSeverity.Error);
    }
}
