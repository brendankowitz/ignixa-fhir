// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

public sealed class ValueSetFilterCheckTests
{
    private static ValidationResult Validate(string json)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        return new ValueSetFilterCheck().Validate(element, new ValidationSettings(), new ValidationState());
    }

    [Fact]
    public void GivenNotSelectableFilterWithNonBooleanValue_WhenValidating_ThenReturnsError()
    {
        // Arrange - notSelectable is a boolean concept-property; '1' is not a boolean.
        var result = Validate("""
        {
            "resourceType": "ValueSet", "status": "active",
            "compose": { "include": [ {
                "system": "http://terminology.hl7.org/CodeSystem/ex-tooth",
                "filter": [ { "property": "notSelectable", "op": "=", "value": "1" } ]
            } ] }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "vsf-1");
    }

    [Fact]
    public void GivenNotSelectableFilterWithBooleanValue_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "ValueSet", "status": "active",
            "compose": { "include": [ {
                "system": "http://snomed.info/sct",
                "filter": [ { "property": "inactive", "op": "=", "value": "true" } ]
            } ] }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenInactiveFilterValueSuppliedByExtension_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - the value is parameterized via the _value primitive extension; no literal to check.
        var result = Validate("""
        {
            "resourceType": "ValueSet", "status": "active",
            "compose": { "include": [ {
                "system": "http://snomed.info/sct",
                "filter": [ { "property": "inactive", "op": "=", "_value": {
                    "extension": [ { "url": "http://hl7.org/fhir/StructureDefinition/cqf-expression",
                        "valueExpression": { "language": "text/fhirpath", "expression": "%p-inactive" } } ] } } ]
            } ] }
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }
}
