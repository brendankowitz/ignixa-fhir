// <copyright file="EmptyArrayCheckTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Checks;

/// <summary>
/// Tests for EmptyArrayCheck.
/// </summary>
public class EmptyArrayCheckTests
{
    private static ValidationResult Validate(string json)
    {
        var node = JsonNode.Parse(json);
        var sourceNode = JsonNodeSourceNode.Create(node);
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var check = new EmptyArrayCheck();

        return check.Validate(element, new ValidationSettings(), new ValidationState());
    }

    [Fact]
    public void GivenEmptyArrayNestedInsideCodeableConcept_WhenValidating_ThenReturnsError()
    {
        // Arrange - CodeableConcept is never expanded into its own nested schema, so this gap is
        // only caught by a raw-JSON walk from the resource root.
        var result = Validate("""
        {
            "resourceType": "DocumentReference",
            "status": "current",
            "category": [{ "coding": [] }],
            "content": [
                { "attachment": { "contentType": "text/plain", "data": "Zm9v" } }
            ]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path == "DocumentReference.category[0].coding");
    }

    [Fact]
    public void GivenNoEmptyArrays_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "Patient",
            "name": [{ "family": "Smith", "given": ["Jane"] }]
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenEmptyContainedArray_WhenValidating_ThenReturnsSuccess()
    {
        // Arrange - "contained" is excluded: established behavior tolerates an empty array here.
        var result = Validate("""
        {
            "resourceType": "Patient",
            "contained": []
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void GivenEmptyTopLevelArray_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var result = Validate("""
        {
            "resourceType": "Patient",
            "name": []
        }
        """);

        // Act / Assert
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path == "Patient.name");
    }
}
