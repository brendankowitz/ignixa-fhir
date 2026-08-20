/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The collection functions and the equality operators must answer the same question about quantities.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Holds every collection operation that uses equality to the answer the <c>=</c> operator gives, for
/// quantities.
/// </summary>
/// <remarks>
/// <para>
/// This is the quantity half of what <see cref="TemporalCollectionEqualityTests"/> covers for temporals,
/// and the two halves were each missing the branch the other had. <c>FunctionHelpers.AreElementsEqual</c>
/// routed temporals through <c>TemporalOperand</c> but had no quantity branch at all, so a quantity fell
/// through to <see cref="object.Equals(object)"/> on the carrier - which compares the unit as text.
/// <c>1 'm' = 100 'cm'</c> was therefore <see langword="true"/> as an operator and <see langword="false"/>
/// as membership, <c>(1 'm' | 100 'cm').distinct()</c> returned two elements, and
/// <c>1 'wk' in (7 'd')</c> was <see langword="false"/>.
/// </para>
/// <para>
/// The conversion is <c>ValueOrdering.TryAlignUnits</c>, the same one <c>&lt;</c>, <c>=</c>, <c>~</c>,
/// <c>sort()</c> and the aggregates reach, so no surface can acquire a private opinion about which units
/// relate. What differs per surface is only how the undecided case collapses, and that distinction is
/// asserted here rather than assumed: <c>=</c> on incompatible units is empty, <c>~</c> is
/// <see langword="false"/>, and membership - which has no third state - is "not the same item".
/// </para>
/// </remarks>
public class QuantityCollectionEqualityTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    /// <summary>
    /// Two Observations whose values are the same quantity written in different units, so that every
    /// assertion below is about unit conversion rather than about the numbers.
    /// </summary>
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
            "valueQuantity": { "value": 2000, "unit": "mg", "system": "http://unitsofmeasure.org", "code": "mg" }
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
    /// A Quantity that declares no unit at all, which is the shape that exposed the reader divergence:
    /// the ordering path refused it, equality reported it unequal to itself, and <c>~</c> read it as the
    /// unity unit.
    /// </summary>
    private const string UnitlessBundleJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "Observation",
            "id": "u1",
            "status": "final",
            "code": { "text": "score" },
            "valueQuantity": { "value": 5 }
          }
        },
        {
          "resource": {
            "resourceType": "Observation",
            "id": "u2",
            "status": "final",
            "code": { "text": "score" },
            "valueQuantity": { "value": 5 }
          }
        }
      ]
    }
    """;

    /// <summary>
    /// One value in two units. Every surface must agree that these are one item.
    /// </summary>
    [Theory]
    [InlineData("1 'm' = 100 'cm'", "true")]
    [InlineData("1 'm' != 100 'cm'", "false")]
    [InlineData("(1 'm' | 100 'cm').count()", "1")]
    [InlineData("(1 'm' | 100 'cm').distinct().count()", "1")]
    [InlineData("1 'm' in (100 'cm')", "true")]
    [InlineData("(100 'cm') contains 1 'm'", "true")]
    [InlineData("(1 'm').intersect(100 'cm').count()", "1")]
    [InlineData("(1 'm').exclude(100 'cm').count()", "0")]
    [InlineData("(1 'm').combine(100 'cm').distinct().count()", "1")]
    [InlineData("(1 'm').combine(100 'cm').isDistinct()", "false")]
    public void GivenOneValueInTwoUnits_WhenComparedByAnySurface_ThenItIsOneItem(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// A calendar-duration keyword and its UCUM equivalent are the same value, and were the case where
    /// the text comparison was least defensible: nothing about "wk" and "d" suggests they are equal.
    /// </summary>
    [Theory]
    [InlineData("1 'wk' = 7 'd'", "true")]
    [InlineData("(7 'd' | 1 'wk').distinct().count()", "1")]
    [InlineData("1 'wk' in (7 'd')", "true")]
    [InlineData("(1 'wk').exclude(7 'd').count()", "0")]
    [InlineData("(1 'wk').intersect(7 'd').count()", "1")]
    public void GivenACalendarDurationAndItsUcumEquivalent_WhenComparedByAnySurface_ThenItIsOneItem(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// Incompatible units are a first-class state rather than a failure to compare - <c>comparable()</c>
    /// reports it directly (official <c>Comparable2</c>) - and each operator collapses it its own way.
    /// <c>=</c> declines to answer.
    /// </summary>
    [Theory]
    [InlineData("1 'm' = 1 's'")]
    [InlineData("1 'm' != 1 's'")]
    [InlineData("1 'mg' = 5")]
    public void GivenIncompatibleUnits_WhenUsingEquality_ThenItYieldsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// Equivalence has no third state, so the same incompatibility that makes <c>=</c> empty makes
    /// <c>~</c> decidably false. Asserting both is the point: they must not be made to agree.
    /// </summary>
    [Theory]
    [InlineData("1 'm' ~ 1 's'", "false")]
    [InlineData("1 'm' !~ 1 's'", "true")]
    [InlineData("1 'cm'.comparable(1 '[s]')", "false")]
    [InlineData("1 'cm'.comparable(1 '[in_i]')", "true")]
    public void GivenIncompatibleUnits_WhenUsingEquivalence_ThenItIsFalse(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// Membership has no third state either, and collapses the undecided case to "not the same item" -
    /// the same way the temporal branch collapses an indeterminate comparison. Deduplicating instead
    /// would discard a value the engine refuses to call equal.
    /// </summary>
    [Theory]
    [InlineData("(1 'm' | 1 's').distinct().count()", "2")]
    [InlineData("1 'm' in (1 's')", "false")]
    [InlineData("(1 'm').intersect(1 's').count()", "0")]
    [InlineData("(1 'm').exclude(1 's').count()", "1")]
    [InlineData("(1 'mg' | 5).distinct().count()", "2")]
    public void GivenIncompatibleUnits_WhenUsedForMembership_ThenTheItemsStayDistinct(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// The fix must not collapse quantities that merely share a dimension.
    /// </summary>
    [Theory]
    [InlineData("(1 'm' | 2 'm').distinct().count()", "2")]
    [InlineData("(1 'm' | 101 'cm').distinct().count()", "2")]
    [InlineData("(1 'm' | 1 'm').distinct().count()", "1")]
    [InlineData("1 'm' in (2 'm')", "false")]
    [InlineData("(1 'm' | 2 'm' | 100 'cm').distinct().count()", "2")]
    [InlineData("5 '1' = 5", "true")]
    [InlineData("(5 '1' | 5).distinct().count()", "1")]
    public void GivenQuantitiesThatDifferInValue_WhenDeduplicated_ThenTheyStayDistinct(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// A Quantity read out of a resource is a complex element whose own <c>Value</c> is
    /// <see langword="null"/> and whose value and unit are children, so a fixture built only from
    /// literals cannot see whether the equality path reads children at all - which is precisely what hid
    /// the earlier quantity bugs. These two elements are 2000 mg and 2 g.
    /// </summary>
    [Theory]
    [InlineData("Bundle.entry.resource.value.first() = Bundle.entry.resource.value.last()", "true")]
    [InlineData("Bundle.entry.resource.value.distinct().count()", "1")]
    [InlineData("Bundle.entry.resource.value.isDistinct()", "false")]
    [InlineData("Bundle.entry.resource.value.first() in Bundle.entry.resource.value.last()", "true")]
    [InlineData("2 'g' in Bundle.entry.resource.value", "true")]
    [InlineData("Bundle.entry.resource.value.intersect(2 'g').count()", "1")]
    [InlineData("Bundle.entry.resource.value.exclude(2 'g').count()", "0")]
    [InlineData("(Bundle.entry.resource.value | 2 'g').distinct().count()", "1")]
    [InlineData("Bundle.entry.resource.value.first() ~ Bundle.entry.resource.value.last()", "true")]
    [InlineData("1 's' in Bundle.entry.resource.value", "false")]
    public void GivenResourceBackedQuantities_WhenComparedByAnySurface_ThenUnitsAreConverted(string expression, string expected)
    {
        // Arrange
        var bundle = Parse(BundleJson);

        // Act
        var result = EvaluateAgainst(bundle, expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// A Quantity with a value and no unit is the unity unit, and the three readers of a Quantity element
    /// disagreed about it: <c>=</c> reported the element unequal to itself, <c>&lt;</c> threw, and
    /// <c>~</c> answered true. Equal to itself is the assertion that fails on any two of the three.
    /// </summary>
    [Theory]
    [InlineData("Bundle.entry.resource.value.first() = Bundle.entry.resource.value.first()", "true")]
    [InlineData("Bundle.entry.resource.value.first() != Bundle.entry.resource.value.first()", "false")]
    [InlineData("Bundle.entry.resource.value.first() = Bundle.entry.resource.value.last()", "true")]
    [InlineData("Bundle.entry.resource.value.first() ~ Bundle.entry.resource.value.last()", "true")]
    [InlineData("Bundle.entry.resource.value.distinct().count()", "1")]
    [InlineData("Bundle.entry.resource.value.first() <= Bundle.entry.resource.value.last()", "true")]
    [InlineData("Bundle.entry.resource.value.first() = 5", "true")]
    public void GivenAUnitlessResourceQuantity_WhenComparedByAnySurface_ThenItIsTheUnityUnit(string expression, string expected)
    {
        // Arrange
        var bundle = Parse(UnitlessBundleJson);

        // Act
        var result = EvaluateAgainst(bundle, expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// Non-quantity equality must be unaffected: the new branch may only add decisions.
    /// </summary>
    [Theory]
    [InlineData("('a' | 'a').distinct().count()", "1")]
    [InlineData("('a' | 'b').distinct().count()", "2")]
    [InlineData("(1 | 1.0).distinct().count()", "1")]
    [InlineData("(1 | 2).distinct().count()", "2")]
    [InlineData("1 'mg' = 'x'", "false")]
    [InlineData("(1 'mg' | 'x').distinct().count()", "2")]
    [InlineData("(@2012-01-01 | @2012-01-01).distinct().count()", "1")]
    public void GivenNonQuantityValues_WhenDeduplicated_ThenEqualityIsUnchanged(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);

    private List<IElement> Evaluate(string expression)
    {
        var parsed = _parser.Parse(expression);
        return _evaluator.Evaluate(new ScalarRoot(), parsed).ToList();
    }

    private List<IElement> EvaluateAgainst(IElement subject, string expression)
    {
        var parsed = _parser.Parse(expression);
        return _evaluator.Evaluate(subject, parsed, DifferentialFixture.CreateContext(subject)).ToList();
    }

    private sealed class ScalarRoot : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "integer";
        public object Value => 0;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
