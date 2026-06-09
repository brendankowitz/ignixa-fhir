// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Pins the bug where a profile that constrains a choice element (e.g. <c>value[x]</c> with
/// <c>min=1</c>) produced a <c>CardinalityCheck</c> keyed on the literal <c>"value[x]"</c> name.
/// <c>IElement.Children</c> only performs polymorphic <c>[x]</c> expansion when the requested
/// name has no <c>[x]</c> suffix, so the check counted zero concrete children and raised a
/// spurious cardinality error even when a concrete value (e.g. <c>valueString</c>) was present.
/// </summary>
public class ChoiceElementCardinalityTests
{
    // Minimal StructureDefinition (snapshot) for an Observation profile that makes value[x]
    // required (min=1). The adapter names the choice element "value[x]" with IsChoiceElement=true.
    private const string ObservationValueRequiredSd = """
        {
          "resourceType": "StructureDefinition",
          "id": "obs-value-required",
          "url": "http://example.org/StructureDefinition/obs-value-required",
          "type": "Observation",
          "kind": "resource",
          "abstract": false,
          "snapshot": {
            "element": [
              { "path": "Observation", "min": 0, "max": "*" },
              {
                "path": "Observation.value[x]",
                "min": 1,
                "max": "1",
                "type": [ { "code": "string" }, { "code": "Quantity" } ]
              }
            ]
          }
        }
        """;

    private readonly ISchema _schema = new R4CoreSchemaProvider();

    private ValidationSchema BuildValueRequiredSchema()
    {
        var adaptedRoot = new StructureDefinitionTypeAdapter().Adapt(ObservationValueRequiredSd, "4.0.1");
        adaptedRoot.ShouldNotBeNull();
        return new StructureDefinitionSchemaBuilder().BuildSchema(adaptedRoot!, _schema, terminologyService: null);
    }

    private IElement ParseElement(string resourceJson)
    {
        var json = JsonNode.Parse(resourceJson);
        return JsonNodeSourceNode.Create(json!).ToElement(_schema);
    }

    [Theory]
    [InlineData(ValidationDepth.Compatibility)]
    [InlineData(ValidationDepth.Spec)]
    public void GivenRequiredChoiceElementWithConcreteValuePresent_WhenValidating_ThenNoCardinalityError(ValidationDepth depth)
    {
        var schema = BuildValueRequiredSchema();
        var element = ParseElement("""
            { "resourceType": "Observation", "status": "final", "valueString": "present" }
            """);

        var result = schema.Validate(element, new ValidationSettings { Depth = depth }, new ValidationState());

        result.Issues
            .Where(i => i.Severity == IssueSeverity.Error)
            .ShouldNotContain(
                i => i.Path.Contains("value", StringComparison.Ordinal) && i.Code == "cardinality-violation",
                customMessage: "A concrete choice value (valueString) satisfies value[x] min=1 — no cardinality error expected");
    }

    [Fact]
    public void GivenRequiredChoiceElementAbsent_WhenValidatingAtSpecDepth_ThenReportsCardinalityError()
    {
        var schema = BuildValueRequiredSchema();
        var element = ParseElement("""
            { "resourceType": "Observation", "status": "final" }
            """);

        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec }, new ValidationState());

        result.Issues
            .Where(i => i.Severity == IssueSeverity.Error)
            .ShouldContain(
                i => i.Path.Contains("value", StringComparison.Ordinal) && i.Code == "cardinality-violation",
                customMessage: "An absent required choice element (value[x] min=1) must produce a cardinality error");
    }
}
