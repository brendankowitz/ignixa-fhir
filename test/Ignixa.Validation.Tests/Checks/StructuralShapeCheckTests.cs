// <copyright file="StructuralShapeCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// fhir262 "structural shape" conformance tests: malformed resources whose raw JSON shape
/// violates the element definition (array-vs-scalar, null, ele-1 emptiness, primitive-vs-object)
/// must be rejected with the exact issue.expression from the suite.
/// </summary>
public class StructuralShapeCheckTests
{
    private readonly IValidationSchemaResolver _schemaResolver;

    public StructuralShapeCheckTests()
    {
        ISchema schema = new R4CoreSchemaProvider();
        var inner = new StructureDefinitionSchemaResolver(schema);
        _schemaResolver = new CachedValidationSchemaResolver(inner);
    }

    private ValidationResult Validate(string resourceJson)
    {
        var json = JsonNode.Parse(resourceJson);
        var sourceNode = JsonNodeSourceNode.Create(json!);
        var resourceType = sourceNode.ResourceType ?? sourceNode.Name;
        var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
        var schema = _schemaResolver.GetSchema(canonicalUrl)
            ?? throw new InvalidOperationException($"Schema not found for {resourceType}");

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        return schema.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, new ValidationState());
    }

    private void AssertInvalidWithExpression(string resourceJson, string expectedExpression)
    {
        var result = Validate(resourceJson);

        result.IsValid.ShouldBeFalse(
            $"Expected invalid but passed. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
        result.Issues.ShouldContain(
            i => i.Path == expectedExpression,
            $"Expected an issue with expression '{expectedExpression}'. Actual: {string.Join(", ", result.Issues.Select(i => i.Path))}");
    }

    [Fact]
    public void GivenObjectAtScalarPrimitive_WhenValidating_ThenInvalidAtPatientActive()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","active":{"id":"1"}}""",
            "Patient.active");
    }

    [Fact]
    public void GivenObjectAtPrimitiveArrayItem_WhenValidating_ThenInvalidAtGivenIndex()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":[{"given":[{"id":"1"}]}]}""",
            "Patient.name[0].given[0]");
    }

    [Fact]
    public void GivenSingleObjectForCollection_WhenValidating_ThenInvalidAtPatientName()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":{"family":"Ivan"}}""",
            "Patient.name");
    }

    [Fact]
    public void GivenArrayForScalar_WhenValidating_ThenInvalidAtPatientGender()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","gender":["male"]}""",
            "Patient.gender");
    }

    [Fact]
    public void GivenEmptyArray_WhenValidating_ThenInvalidAtPatientName()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":[]}""",
            "Patient.name");
    }

    [Fact]
    public void GivenEmptyObjectArrayItem_WhenValidating_ThenInvalidAtNameIndex()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":[{}]}""",
            "Patient.name[0]");
    }

    [Fact]
    public void GivenNullArrayItem_WhenValidating_ThenInvalidAtNameIndex()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":[null]}""",
            "Patient.name[0]");
    }

    [Fact]
    public void GivenEmptyComplexObject_WhenValidating_ThenInvalidAtMaritalStatus()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","maritalStatus":{}}""",
            "Patient.maritalStatus");
    }

    [Fact]
    public void GivenNullComplexProperty_WhenValidating_ThenInvalidAtMaritalStatus()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","maritalStatus":null}""",
            "Patient.maritalStatus");
    }

    [Fact]
    public void GivenNullPrimitiveProperty_WhenValidating_ThenInvalidAtBirthDate()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","birthDate":null}""",
            "Patient.birthDate");
    }

    [Fact]
    public void GivenPrimitiveAtComplexElement_WhenValidating_ThenInvalidAtPatientName()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":"John"}""",
            "Patient.name");
    }

    [Fact]
    public void GivenNullPrimitiveExtensionShadow_WhenValidating_ThenInvalidAtGender()
    {
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","_gender":null}""",
            "Patient.gender");
    }

    // -------------------------------------------------------------------------
    // Valid shapes that MUST continue to pass (no over-rejection).
    // -------------------------------------------------------------------------

    [Fact]
    public void GivenValidPatient_WhenValidating_ThenValid()
    {
        var result = Validate(
            """{"resourceType":"Patient","active":true,"gender":"male","birthDate":"1970-01-01","name":[{"family":"Ivan","given":["John","Q"]}],"maritalStatus":{"text":"Married"}}""");

        result.IsValid.ShouldBeTrue(
            $"Expected valid. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    [Fact]
    public void GivenPrimitiveWithExtensionShadow_WhenValidating_ThenValid()
    {
        // birthDate:null paired with a valid _birthDate extension is the legal FHIR way to attach
        // an extension without a value - must NOT be rejected as a null primitive.
        var result = Validate(
            """{"resourceType":"Patient","birthDate":null,"_birthDate":{"extension":[{"url":"http://example.org","valueCode":"unknown"}]}}""");

        result.IsValid.ShouldBeTrue(
            $"Expected valid. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    [Fact]
    public void GivenScalarPrimitiveWithShadow_WhenValidating_ThenValid()
    {
        var result = Validate(
            """{"resourceType":"Patient","active":true,"_active":{"extension":[{"url":"http://example.org","valueString":"confirmed"}]}}""");

        result.IsValid.ShouldBeTrue(
            $"Expected valid. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    [Fact]
    public void GivenPrimitiveArrayPairedWithShadowArray_WhenValidating_ThenValid()
    {
        // given is a 0..* primitive-string array; a paired _given shadow array (with a null entry
        // carrying an extension) is legal FHIR and must pass.
        var result = Validate(
            """{"resourceType":"Patient","name":[{"given":["John",null],"_given":[null,{"extension":[{"url":"http://example.org","valueString":"nickname"}]}]}]}""");

        result.IsValid.ShouldBeTrue(
            $"Expected valid. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    // -------------------------------------------------------------------------
    // ele-1 backbone vs datatype: StructuralShapeCheck must NOT raise ele-1 for an
    // empty inline BackboneElement (its required children are enforced elsewhere),
    // while an empty complex datatype MUST raise ele-1.
    // -------------------------------------------------------------------------

    private static ValidationResult RunStructuralShapeCheck(string resourceJson, string elementName, bool isBackbone)
    {
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(resourceJson)!);
        var check = new StructuralShapeCheck(elementName, isPrimitive: false, isCollection: true, isBackbone: isBackbone, primitiveType: string.Empty);
        return check.Validate(
            sourceNode.ToElement(TestSchemaProvider.GetR4Schema()),
            new ValidationSettings { Depth = ValidationDepth.Spec },
            new ValidationState());
    }

    [Fact]
    public void GivenEmptyBackboneElement_WhenStructuralShapeChecking_ThenDoesNotRaiseEle1()
    {
        // Patient.contact is an inline BackboneElement; an empty {} entry must not trip ele-1 here.
        var result = RunStructuralShapeCheck(
            """{"resourceType":"Patient","contact":[{}]}""",
            "contact",
            isBackbone: true);

        result.Issues.ShouldNotContain(
            i => i.Code == "ele-1",
            $"Empty backbone must not raise ele-1 from StructuralShapeCheck. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Code}"))}");
    }

    [Fact]
    public void GivenEmptyDatatypeElement_WhenStructuralShapeChecking_ThenRaisesEle1()
    {
        // A complex datatype object with no content MUST raise ele-1 (contrast with the backbone case).
        var result = RunStructuralShapeCheck(
            """{"resourceType":"Patient","name":[{}]}""",
            "name",
            isBackbone: false);

        result.Issues.ShouldContain(
            i => i.Code == "ele-1",
            $"Empty datatype must raise ele-1. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Code}"))}");
    }

    [Fact]
    public void GivenEmptyContainedArray_WhenValidating_ThenValid()
    {
        // contained:[] is tolerated (an empty contained array carries no resources), in contrast to
        // a required-element empty array like name:[] which is rejected.
        var result = Validate(
            """{"resourceType":"Patient","contained":[]}""");

        result.IsValid.ShouldBeTrue(
            $"Expected valid. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }
}
