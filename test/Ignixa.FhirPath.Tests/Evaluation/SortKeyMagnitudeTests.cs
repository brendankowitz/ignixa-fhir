/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * sort()'s quantity key at magnitudes where converting to the dimension's base unit leaves decimal's range.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers the property the quantity sort key rests on: which bucket a quantity lands in is a function of
/// its unit, never of its value.
/// </summary>
/// <remarks>
/// <para>
/// The key was built by converting the quantity's own value into its dimension's base unit and treating
/// a failure as "UCUM cannot canonicalise this unit". That conflated two different things. A conversion
/// that overflows <see cref="decimal"/> says nothing about the unit, so two quantities in the
/// <em>same</em> unit could land in different buckets, and a large quantity fell out of the dimension
/// grouping entirely and was ordered against its neighbours by the spelling of its unit.
/// </para>
/// <para>
/// The scale now comes from converting one of the unit, so the bucket depends on the unit alone and
/// every pair of commensurable units shares one. The value is then multiplied in, saturating at
/// <see cref="decimal"/>'s bounds rather than throwing - which keeps the key monotone in the true
/// magnitude, so what an out-of-range product costs is a tie rather than an inversion.
/// </para>
/// <para>
/// The fixture is resource-backed because the FHIRPath literal grammar has no exponent notation: <c>1e26
/// 'km'</c> does not parse. JSON numbers do, and this is how such a value actually reaches the engine.
/// </para>
/// </remarks>
public class SortKeyMagnitudeTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    /// <summary>
    /// -1e26 km is -1e29 m, which is past <see cref="decimal.MinValue"/>. It is also, by a very wide
    /// margin, the smaller of the two.
    /// </summary>
    private const string LargeNegativeThenSmallPositive = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        { "resource": { "resourceType": "Observation", "id": "o1", "status": "final", "code": { "text": "d" },
            "valueQuantity": { "value": -1e26, "code": "km", "system": "http://unitsofmeasure.org" } } },
        { "resource": { "resourceType": "Observation", "id": "o2", "status": "final", "code": { "text": "d" },
            "valueQuantity": { "value": 1, "code": "m", "system": "http://unitsofmeasure.org" } } }
      ]
    }
    """;

    private const string SmallPositiveThenLargeNegative = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        { "resource": { "resourceType": "Observation", "id": "o1", "status": "final", "code": { "text": "d" },
            "valueQuantity": { "value": 1, "code": "m", "system": "http://unitsofmeasure.org" } } },
        { "resource": { "resourceType": "Observation", "id": "o2", "status": "final", "code": { "text": "d" },
            "valueQuantity": { "value": -1e26, "code": "km", "system": "http://unitsofmeasure.org" } } }
      ]
    }
    """;

    /// <summary>
    /// The premise: the two really are ordered, and the ordering operator says which way round. Without
    /// this the sort assertion below would just be asserting a preference.
    /// </summary>
    [Fact]
    public void GivenAnOutOfRangeNegativeQuantity_WhenComparedWithAnOperator_ThenItIsTheSmaller()
    {
        // Arrange
        var bundle = Parse(LargeNegativeThenSmallPositive);

        // Act
        var result = bundle
            .Select("Bundle.entry.resource.value.first() > Bundle.entry.resource.value.last()")
            .ShouldHaveSingleItem();

        // Assert
        result.Value.ShouldBe(false);
    }

    /// <summary>
    /// The inversion. A magnitude whose conversion to metres overflows used to leave the canonical
    /// branch, and everything that had left it sorted after everything that had not - which is right for
    /// a large positive value and exactly backwards for a large negative one.
    /// </summary>
    [Theory]
    [InlineData(LargeNegativeThenSmallPositive)]
    [InlineData(SmallPositiveThenLargeNegative)]
    public void GivenAnOutOfRangeNegativeQuantity_WhenSorting_ThenItStillLeads(string json)
    {
        // Arrange
        var bundle = Parse(json);

        // Act
        var result = bundle
            .Select("Bundle.entry.resource.value.sort().code")
            .Select(element => element.Value?.ToString())
            .ToList();

        // Assert
        result.ShouldBe(["km", "m"]);
    }

    /// <summary>
    /// Guard: the saturation must not have flattened ordinary magnitudes. Two quantities well inside
    /// range, in commensurable units, still order by their converted values.
    /// </summary>
    [Fact]
    public void GivenOrdinaryMagnitudesInCommensurableUnits_WhenSorting_ThenTheyStillOrderByValue()
    {
        // Arrange
        var bundle = Parse("""
        {
          "resourceType": "Bundle",
          "type": "collection",
          "entry": [
            { "resource": { "resourceType": "Observation", "id": "o1", "status": "final", "code": { "text": "d" },
                "valueQuantity": { "value": 2, "code": "km", "system": "http://unitsofmeasure.org" } } },
            { "resource": { "resourceType": "Observation", "id": "o2", "status": "final", "code": { "text": "d" },
                "valueQuantity": { "value": 500, "code": "m", "system": "http://unitsofmeasure.org" } } }
          ]
        }
        """);

        // Act
        var result = bundle
            .Select("Bundle.entry.resource.value.sort().code")
            .Select(element => element.Value?.ToString())
            .ToList();

        // Assert
        result.ShouldBe(["m", "km"]);
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);
}
