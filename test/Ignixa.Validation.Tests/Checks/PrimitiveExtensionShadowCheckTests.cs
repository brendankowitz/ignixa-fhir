// <copyright file="PrimitiveExtensionShadowCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// fhir262 "shadow (_x)" conformance tests: FHIR JSON primitive-extension representation rules.
/// A primitive element 'x' may carry a sibling '_x' holding its id/extension as an Element. The
/// shadow is only legal on a primitive, must mirror the value's cardinality (object for a scalar,
/// array aligned 1:1 for a collection), may contain only id/extension, and a null primitive slot
/// paired with a content-less shadow slot violates the empty-value (ele-1) rule.
/// </summary>
public class PrimitiveExtensionShadowCheckTests
{
    private readonly IValidationSchemaResolver _schemaResolver;

    public PrimitiveExtensionShadowCheckTests()
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

    private void AssertValid(string resourceJson)
    {
        var result = Validate(resourceJson);
        result.IsValid.ShouldBeTrue(
            $"Expected valid. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
    }

    private void AssertInvalid(string resourceJson)
    {
        var result = Validate(resourceJson);
        result.IsValid.ShouldBeFalse(
            $"Expected invalid but passed. Issues: {string.Join(", ", result.Issues.Select(i => $"{i.Path}: {i.Message}"))}");
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

    // -------------------------------------------------------------------------
    // INVALID shadow shapes (cases 1-7 from the fhir262 suite).
    // -------------------------------------------------------------------------

    [Fact]
    public void GivenShadowForNonexistentField_WhenValidating_ThenInvalid()
    {
        // Case 1: "_unknown" has no matching declared field.
        AssertInvalid("""{"resourceType":"Patient","_unknown":{"id":"1"}}""");
    }

    [Fact]
    public void GivenShadowOnComplexElement_WhenValidating_ThenInvalidAtPatientName()
    {
        // Case 2: "_name" shadows a complex element; only primitives may carry a shadow.
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","_name":{"id":"1"}}""",
            "Patient.name");
    }

    [Fact]
    public void GivenScalarShadowAsPrimitive_WhenValidating_ThenInvalidAtPatientActive()
    {
        // Case 3: "_active" must be an Element object, not a JSON primitive.
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","_active":"test"}""",
            "Patient.active");
    }

    [Fact]
    public void GivenScalarShadowAsArray_WhenValidating_ThenInvalidAtPatientActive()
    {
        // Case 4: a scalar primitive's shadow must be an object, not an array.
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","_active":[{"id":"test"}]}""",
            "Patient.active");
    }

    [Fact]
    public void GivenRepeatedShadowAsObject_WhenValidating_ThenInvalidAtGiven()
    {
        // Case 5: a repeated primitive's shadow must be an array, not an object.
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":[{"given":["a"],"_given":{"id":"test"}}]}""",
            "Patient.name[0].given");
    }

    [Fact]
    public void GivenShadowElementWithUnknownKey_WhenValidating_ThenInvalidAtGivenIndex()
    {
        // Case 6: a shadow Element may carry only id/extension; "foo" is invalid.
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":[{"given":["a"],"_given":[{"foo":"test"}]}]}""",
            "Patient.name[0].given[0]");
    }

    [Fact]
    public void GivenNullValueWithIdOnlyShadow_WhenValidating_ThenInvalidAtGivenIndex()
    {
        // Case 7: given[0] is null and _given[0] supplies only an id (no value, no extension),
        // leaving the element with neither @value nor children (empty-value / ele-1).
        AssertInvalidWithExpression(
            """{"resourceType":"Patient","name":[{"given":[null,"test"],"_given":[{"id":"test"},null]}]}""",
            "Patient.name[0].given[0]");
    }

    // -------------------------------------------------------------------------
    // VALID shadow shapes that MUST continue to pass (regression guards).
    // -------------------------------------------------------------------------

    [Fact]
    public void GivenScalarValueWithExtensionShadow_WhenValidating_ThenValid()
    {
        AssertValid(
            """{"resourceType":"Patient","birthDate":"1974-12-25","_birthDate":{"extension":[{"url":"http://example.org","valueDateTime":"1974-12-25T14:35:45-05:00"}]}}""");
    }

    [Fact]
    public void GivenScalarValueWithYearOnlyExtensionShadow_WhenValidating_ThenValid()
    {
        AssertValid(
            """{"resourceType":"Patient","birthDate":"1974-12-25","_birthDate":{"extension":[{"url":"http://example.org","valueDateTime":"2020"}]}}""");
    }

    [Fact]
    public void GivenRepeatedValueWithIdOnlyShadow_WhenValidating_ThenValid()
    {
        // given[0] has a value, so an id-only shadow slot is legal (no empty-value violation).
        AssertValid(
            """{"resourceType":"Patient","name":[{"given":["a"],"_given":[{"id":"test"}]}]}""");
    }

    [Fact]
    public void GivenRepeatedValueWithTrailingNullShadow_WhenValidating_ThenValid()
    {
        AssertValid(
            """{"resourceType":"Patient","name":[{"given":["a"],"_given":[{"id":"test"},null]}]}""");
    }

    [Fact]
    public void GivenRepeatedValueWithAllNullShadow_WhenValidating_ThenValid()
    {
        AssertValid(
            """{"resourceType":"Patient","name":[{"given":["a"],"_given":[null,null]}]}""");
    }

    [Fact]
    public void GivenNullValueFilledByExtensionShadow_WhenValidating_ThenValid()
    {
        // given[0] is null but _given[0] supplies an extension, so the element has children.
        AssertValid(
            """{"resourceType":"Patient","name":[{"given":[null,"test"],"_given":[{"extension":[{"url":"http://example.org","valueDateTime":"2020"}]},null]}]}""");
    }
}
