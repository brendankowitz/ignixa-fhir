/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * sum() and avg() must answer the same question the same way whether one item reaches them or two.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers the single-element path through <c>sum()</c> and <c>avg()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both functions used to shortcut a one-element collection: check the type, hand the element straight
/// back. The check admitted anything <c>IsNumericValued</c> reported, which includes a
/// <see cref="string"/> under the declared type <c>decimal</c> - how a FHIR decimal too large for
/// <see cref="decimal"/> arrives off the wire, because the reader keeps the source text rather than
/// losing the value. So one such element came back as a raw string and two came back as empty: the same
/// data, with the answer decided by cardinality.
/// </para>
/// <para>
/// For <c>avg()</c> that also contradicted its own declared return type - §avg() answers Decimal or
/// Quantity, and the shortcut returned a String. The type gate's stated purpose was to stop exactly this
/// class of asymmetry, and it closed only the half where the shortcut threw.
/// </para>
/// <para>
/// The fixture is a real resource rather than a hand-built element, because the out-of-range decimal has
/// to arrive the way the reader actually delivers it for the case to be the one that bites.
/// </para>
/// </remarks>
public class AggregateCardinalityTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private const string OneOutOfRangeDecimal = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        { "resource": { "resourceType": "Observation", "id": "o1", "status": "final",
            "code": { "text": "d" }, "valueQuantity": { "value": 1e30, "code": "mg" } } }
      ]
    }
    """;

    private const string TwoOutOfRangeDecimals = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        { "resource": { "resourceType": "Observation", "id": "o1", "status": "final",
            "code": { "text": "d" }, "valueQuantity": { "value": 1e30, "code": "mg" } } },
        { "resource": { "resourceType": "Observation", "id": "o2", "status": "final",
            "code": { "text": "d" }, "valueQuantity": { "value": 1e30, "code": "mg" } } }
      ]
    }
    """;

    /// <summary>
    /// The premise: the reader really does hand this over as text under a numeric declared type. Without
    /// it the rest of the class would be asserting about a fixture that never occurs.
    /// </summary>
    [Fact]
    public void GivenADecimalTooLargeForTheType_WhenRead_ThenItArrivesAsTextUnderTheNumericType()
    {
        // Arrange
        var bundle = Parse(OneOutOfRangeDecimal);

        // Act
        var element = bundle.Select("Bundle.entry.resource.value.value").ShouldHaveSingleItem();

        // Assert
        element.InstanceType.ShouldBe("decimal");
        element.Value.ShouldBeOfType<string>();
    }

    [Theory]
    [InlineData("sum")]
    [InlineData("avg")]
    public void GivenOneUnreadableDecimal_WhenTotalling_ThenItAnswersAsItDoesForTwo(string function)
    {
        // Arrange
        var one = Parse(OneOutOfRangeDecimal);
        var two = Parse(TwoOutOfRangeDecimals);

        // Act
        var fromOne = one.Select($"Bundle.entry.resource.value.value.{function}()").ToList();
        var fromTwo = two.Select($"Bundle.entry.resource.value.value.{function}()").ToList();

        // Assert
        fromTwo.ShouldBeEmpty();
        fromOne.ShouldBeEmpty();
    }

    /// <summary>
    /// §avg() answers Decimal or Quantity. The shortcut returned a String for the one-element case, so
    /// this pins the return type directly rather than only through the two-element comparison.
    /// </summary>
    [Fact]
    public void GivenOneUnreadableDecimal_WhenAveraged_ThenItDoesNotReturnAString()
    {
        // Arrange
        var one = Parse(OneOutOfRangeDecimal);

        // Act
        var result = one.Select("Bundle.entry.resource.value.value.avg()").ToList();

        // Assert
        result.ShouldNotContain(element => element.Value is string);
    }

    /// <summary>
    /// Guard: the shortcut existed to promote a lone Integer, and dropping it must not lose that.
    /// §avg(): "When used with Integer or Long, the arguments will be implicitly converted to Decimal
    /// before evaluation."
    /// </summary>
    [Theory]
    [InlineData("(5).avg()")]
    [InlineData("(5L).avg()")]
    public void GivenASingleInteger_WhenAveraged_ThenItIsPromotedToDecimal(string expression)
    {
        // Act
        var result = Evaluate(expression).ShouldHaveSingleItem();

        // Assert
        result.Value.ShouldBeOfType<decimal>().ShouldBe(5m);
        result.InstanceType.ShouldBe("decimal");
    }

    /// <summary>
    /// Guard: a lone summable element still totals to itself, and to the type its collection would give.
    /// </summary>
    [Theory]
    [InlineData("(5).sum()", 5)]
    [InlineData("(5 | 0).sum()", 5)]
    public void GivenASingleInteger_WhenSummed_ThenItTotalsToItselfAsAnInteger(string expression, int expected)
    {
        // Act
        var result = Evaluate(expression).ShouldHaveSingleItem();

        // Assert
        result.Value.ShouldBe(expected);
        result.InstanceType.ShouldBe("integer");
    }

    /// <summary>
    /// Guard: the type gate still refuses a lone element no arithmetic relates, which is the half of the
    /// asymmetry that was already closed.
    /// </summary>
    [Theory]
    [InlineData("('apple').sum()")]
    [InlineData("('apple').avg()")]
    [InlineData("(true).sum()")]
    [InlineData("(true).avg()")]
    public void GivenASingleUnsummableElement_WhenTotalling_ThenItStillThrows(string expression)
    {
        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    /// <summary>
    /// The documented divergence from §sum()'s "All items in the input collection SHALL be the same type,
    /// otherwise an exception is thrown", pinned so that it is a decision rather than an accident.
    /// </summary>
    /// <remarks>
    /// A literal reading of that SHALL would make every row here an error. It cannot be the intended one:
    /// §avg() requires Integer and Long to be implicitly converted to Decimal, and §Math requires
    /// <c>3 'm' + 3 'cm'</c> to add, both in the same document. The rule applied is the SHALL after
    /// FHIRPath's implicit conversions - which still throws for a String beside an Integer, as the guard
    /// above pins. Continuous build off <c>master</c>, §Aggregates marked <c>{:.stu}</c>; checked
    /// 2026-08-19.
    /// </remarks>
    [Theory]
    [InlineData("(1 | 2.5).sum()", 3.5)]
    [InlineData("(1 | 2L | 3.0).sum()", 6.0)]
    [InlineData("(1 | 2.5).avg()", 1.75)]
    public void GivenNumericTypesThatDiffer_WhenTotalling_ThenTheImplicitConversionsApplyRatherThanAnError(
        string expression,
        double expected)
    {
        // Act
        var result = Evaluate(expression).ShouldHaveSingleItem();

        // Assert
        result.Value.ShouldBeOfType<decimal>().ShouldBe((decimal)expected);
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);

    private static List<IElement> Evaluate(string expression) =>
        Parse(OneOutOfRangeDecimal).Select(expression).ToList();
}
