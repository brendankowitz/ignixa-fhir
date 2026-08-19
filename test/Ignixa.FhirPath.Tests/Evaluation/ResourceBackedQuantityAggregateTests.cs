/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * min(), max(), sum(), avg() and sort() over quantities that came off the wire rather than out of a
 * literal.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers the aggregate functions and <c>sort()</c> on resource-backed <c>Quantity</c> elements.
/// </summary>
/// <remarks>
/// <para>
/// A FHIRPath quantity literal puts a <see cref="FhirQuantity"/> in <c>IElement.Value</c>. A Quantity read
/// out of a resource does not: it is a complex element whose <c>Value</c> is <see langword="null"/> and
/// whose value, unit and code are children. Everything here used to screen operands on <c>Value</c> alone,
/// so every one of these expressions answered empty - or, for <c>sort()</c>, left the collection in
/// arrival order - on data whose individual elements the <c>&lt;</c> operator handled correctly, because
/// that operator alone read the children.
/// </para>
/// <para>
/// This is the shape the data actually takes, and <c>Observation.value.max()</c> is a query a server
/// really receives, so each case asserts against the answer <c>&lt;</c> gives on the same two elements
/// rather than against a number chosen here.
/// </para>
/// </remarks>
public class ResourceBackedQuantityAggregateTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private const string BundleJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "Observation",
            "id": "o1",
            "status": "final",
            "code": { "text": "dose" },
            "valueQuantity": { "value": 150, "unit": "mg", "system": "http://unitsofmeasure.org", "code": "mg" }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "o2",
            "status": "final",
            "code": { "text": "dose" },
            "valueQuantity": { "value": 2, "unit": "g", "system": "http://unitsofmeasure.org", "code": "g" }
          }
        }
      ]
    }
    """;

    /// <summary>
    /// The premise the rest of the class rests on: the operator can read these elements, so nothing about
    /// the data explains the empty results the functions used to give.
    /// </summary>
    [Fact]
    public void GivenResourceBackedQuantities_WhenComparedWithAnOperator_ThenTheyOrder()
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = bundle.Select("Bundle.entry.resource.value.first() < 10 'g'").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedQuantities_WhenMin_ThenSelectsTheSmaller()
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = bundle.Select("Bundle.entry.resource.value.min().value").Single();

        // Assert
        result.Value.ShouldBe(150m);
    }

    [Fact]
    public void GivenResourceBackedQuantities_WhenMax_ThenSelectsTheLarger()
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = bundle.Select("Bundle.entry.resource.value.max().value").Single();

        // Assert
        result.Value.ShouldBe(2m);
    }

    /// <summary>
    /// The cross-check that the extreme is the one the operator agrees with, rather than whichever element
    /// happened to arrive first.
    /// </summary>
    [Fact]
    public void GivenResourceBackedQuantities_WhenMinAndMax_ThenTheOperatorAgreesTheyAreOrdered()
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = bundle
            .Select("Bundle.entry.resource.value.min() < Bundle.entry.resource.value.max()")
            .Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenResourceBackedQuantities_WhenSum_ThenTotalsInTheMostGranularUnit()
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = bundle.Select("Bundle.entry.resource.value.sum()").Single();

        // Assert
        var quantity = result.Value.ShouldBeOfType<FhirQuantity>();
        quantity.Value.ShouldBe(2150m);
        quantity.Unit.ShouldBe("mg");
    }

    [Fact]
    public void GivenResourceBackedQuantities_WhenAvg_ThenAveragesInTheMostGranularUnit()
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = bundle.Select("Bundle.entry.resource.value.avg()").Single();

        // Assert
        var quantity = result.Value.ShouldBeOfType<FhirQuantity>();
        quantity.Value.ShouldBe(1075m);
        quantity.Unit.ShouldBe("mg");
    }

    /// <summary>
    /// sort() screened its keys on <c>Value</c> too, so a collection of resource-backed quantities was
    /// entirely missing keys and came back in arrival order. Written so the two disagree: 150 mg arrives
    /// first and is the smaller, so a stable no-op sort gives the right answer for the wrong reason -
    /// hence the descending assertion as well.
    /// </summary>
    [Fact]
    public void GivenResourceBackedQuantities_WhenSortingDescending_ThenTheLargerLeads()
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = bundle
            .Select("Bundle.entry.resource.value.sort(-$this).value")
            .Select(element => element.Value)
            .ToList();

        // Assert
        result.ShouldBe([2m, 150m]);
    }

    [Fact]
    public void GivenResourceBackedQuantities_WhenSortingAscending_ThenTheSmallerLeads()
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = bundle
            .Select("Bundle.entry.resource.value.sort().value")
            .Select(element => element.Value)
            .ToList();

        // Assert
        result.ShouldBe([150m, 2m]);
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);
}
