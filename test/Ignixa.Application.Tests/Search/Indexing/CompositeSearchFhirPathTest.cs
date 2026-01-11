// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.Specification.Generated;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Tests specifically for FHIRPath expressions used in composite search parameters.
/// </summary>
public class CompositeSearchFhirPathTest
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    [Fact]
    public void GivenObservation_WhenEvaluatingObservationExpression_ThenReturnsObservation()
    {
        // Arrange
        var observation = ObservationBuilder.Create(_schemaProvider)
            .WithCode("9272-6", "http://loinc.org", "1 minute Apgar Score")
            .WithQuantityValue(10, "{score}", "http://unitsofmeasure.org")
            .WithSubject("Patient/test-patient")
            .WithStatus("final")
            .Build();

        var element = observation.ToElement(_schemaProvider);

        Console.WriteLine($"Element InstanceType: {element.InstanceType}");

        // Act: Evaluate expression "Observation" (root expression for code-value-quantity)
        var rootElements = element.Select("Observation").ToList();

        // Assert
        Console.WriteLine($"Root expression 'Observation' returned {rootElements.Count} elements");
        rootElements.ShouldNotBeEmpty("Expression 'Observation' should return the Observation itself");
        rootElements.Count.ShouldBe(1, "Should return exactly one element");
        rootElements[0].InstanceType.ShouldBe("Observation", "Should return Observation element");
    }

    [Fact]
    public void GivenObservation_WhenEvaluatingCodeExpression_ThenReturnsCodeableConcept()
    {
        // Arrange
        var observation = ObservationBuilder.Create(_schemaProvider)
            .WithCode("9272-6", "http://loinc.org", "1 minute Apgar Score")
            .WithQuantityValue(10, "{score}", "http://unitsofmeasure.org")
            .WithSubject("Patient/test-patient")
            .WithStatus("final")
            .Build();

        var element = observation.ToElement(_schemaProvider);
        var rootElements = element.Select("Observation").ToList();
        rootElements.ShouldNotBeEmpty();

        // Act: Evaluate component 0 expression "code"
        var codeElements = rootElements[0].Select("code").ToList();

        // Assert
        Console.WriteLine($"Code expression 'code' returned {codeElements.Count} elements");
        codeElements.ShouldNotBeEmpty("Expression 'code' should return code element");
        codeElements.Count.ShouldBe(1);
        codeElements[0].InstanceType.ShouldBe("CodeableConcept");
    }

    [Fact]
    public void GivenObservation_WhenEvaluatingValueAsQuantityExpression_ThenReturnsQuantity()
    {
        // Arrange
        var observation = ObservationBuilder.Create(_schemaProvider)
            .WithCode("9272-6", "http://loinc.org", "1 minute Apgar Score")
            .WithQuantityValue(10, "{score}", "http://unitsofmeasure.org")
            .WithSubject("Patient/test-patient")
            .WithStatus("final")
            .Build();

        var element = observation.ToElement(_schemaProvider);
        var rootElements = element.Select("Observation").ToList();
        rootElements.ShouldNotBeEmpty();

        // Act: Evaluate component 1 expression "value.as(Quantity)"
        var valueElements = rootElements[0].Select("value.as(Quantity)").ToList();

        // Assert
        Console.WriteLine($"Value expression 'value.as(Quantity)' returned {valueElements.Count} elements");
        valueElements.ShouldNotBeEmpty("Expression 'value.as(Quantity)' should return quantity element");
        valueElements.Count.ShouldBe(1);
        valueElements[0].InstanceType.ShouldBe("Quantity");

        // Verify the quantity values
        var quantityValue = valueElements[0].Scalar("value");
        var quantitySystem = valueElements[0].Scalar("system");
        var quantityCode = valueElements[0].Scalar("code");

        Console.WriteLine($"  Quantity value: {quantityValue}");
        Console.WriteLine($"  Quantity system: {quantitySystem}");
        Console.WriteLine($"  Quantity code: {quantityCode}");

        quantityValue.ShouldBe(10m);
        quantitySystem.ShouldBe("http://unitsofmeasure.org");
        quantityCode.ShouldBe("{score}");
    }

    [Fact]
    public void GivenObservation_WhenNavigatingToValueWithoutAs_ThenReturnsValueQuantity()
    {
        // Arrange
        var observation = ObservationBuilder.Create(_schemaProvider)
            .WithCode("9272-6", "http://loinc.org", "1 minute Apgar Score")
            .WithQuantityValue(10, "{score}", "http://unitsofmeasure.org")
            .WithSubject("Patient/test-patient")
            .WithStatus("final")
            .Build();

        var element = observation.ToElement(_schemaProvider);

        // Act: Navigate to "value" (polymorphic element)
        var valueChildren = element.Children("value").ToList();

        // Assert
        Console.WriteLine($"Children('value') returned {valueChildren.Count} elements");
        valueChildren.ShouldNotBeEmpty("Should find valueQuantity");
        valueChildren.Count.ShouldBe(1);
        valueChildren[0].Name.ShouldBe("valueQuantity", "Name should be suffixed");
        valueChildren[0].InstanceType.ShouldBe("Quantity");
    }
}
