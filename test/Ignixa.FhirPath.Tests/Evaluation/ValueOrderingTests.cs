/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The comparison rule itself, at the surface where zero and indeterminate are still distinguishable.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers <see cref="ValueOrdering"/> directly, rather than through the functions that call it.
/// </summary>
/// <remarks>
/// <para>
/// The distinction between a zero and a <see langword="null"/> result is the whole point of the tri-state
/// return, and it is invisible from outside: <c>sort()</c> resolves both to a position and <c>min()</c>
/// resolves both to "the incumbent stands". So a test that goes through either function cannot tell a
/// comparison that decided the values are equal from one that declined to decide, which is exactly the
/// confusion that produced an intransitive comparer. These pin it at the source.
/// </para>
/// <para>
/// <see cref="ValueOrdering.CompareForSort"/> is here too, because its contract - a total order that
/// agrees with FHIRPath wherever FHIRPath is determinate - is a statement about pairs, not about the
/// sorted output of any one collection.
/// </para>
/// </remarks>
public class ValueOrderingTests
{
    [Fact]
    public void GivenOverlappingPartialPrecisionTemporals_WhenComparing_ThenTheResultIsIndeterminateNotEqual()
    {
        // Arrange
        var year = Temporal("2012", "date");
        var month = Temporal("2012-01", "date");

        // Act
        var result = ValueOrdering.CompareValues(year, month, "min()");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenTwoSpellingsOfOneInstant_WhenComparing_ThenTheResultIsZeroNotIndeterminate()
    {
        // Arrange
        var withOffset = Temporal("2012-01-01T20:00:00+10:00", "dateTime");
        var withZulu = Temporal("2012-01-01T10:00:00Z", "dateTime");

        // Act
        var result = ValueOrdering.CompareValues(withOffset, withZulu, "min()");

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public void GivenAFloatingAndAFixedDateTime_WhenComparing_ThenTheResultIsIndeterminate()
    {
        // Arrange
        var floating = Temporal("2012-01-01T10:00:00", "dateTime");
        var fixedInstant = Temporal("2012-01-01T10:00:00Z", "dateTime");

        // Act
        var result = ValueOrdering.CompareValues(floating, fixedInstant, "min()");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenQuantitiesInUnrelatedDimensions_WhenComparing_ThenTheResultIsIndeterminateNotEqual()
    {
        // Arrange
        var mass = Quantity(1m, "kg");
        var length = Quantity(1m, "m");

        // Act
        var result = ValueOrdering.CompareValues(mass, length, "min()");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenTheSameLengthInTwoUnits_WhenComparing_ThenTheResultIsZero()
    {
        // Arrange
        var metres = Quantity(1m, "m");
        var centimetres = Quantity(100m, "cm");

        // Act
        var result = ValueOrdering.CompareValues(metres, centimetres, "min()");

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public void GivenAnIntegerAndAnEquivalentDecimal_WhenComparing_ThenTheResultIsZero()
    {
        // Arrange
        var integer = Element(1, "integer");
        var equivalent = Element(1.0m, "decimal");

        // Act
        var result = ValueOrdering.CompareValues(integer, equivalent, "min()");

        // Assert
        result.ShouldBe(0);
    }

    /// <summary>
    /// The indeterminate pair still has to be ordered by sort(), and the answer has to be the same one the
    /// determinate direction would give if there were one - coarsest first, so that the interval that
    /// contains the other leads it.
    /// </summary>
    [Fact]
    public void GivenOverlappingPartialPrecisionTemporals_WhenComparingForSort_ThenTheCoarserLeads()
    {
        // Arrange
        var year = Temporal("2012", "date");
        var month = Temporal("2012-01", "date");

        // Act
        var forward = ValueOrdering.CompareForSort(year, month, "sort()");
        var backward = ValueOrdering.CompareForSort(month, year, "sort()");

        // Assert
        forward.ShouldBeLessThan(0);
        backward.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Total does not mean "no ties". Values FHIRPath calls equal must still compare zero, or a stable
    /// sort would reorder them and <c>sort()</c>'s own definition of equality - "items are considered
    /// equal if and only if the equals (=) operator returns true" - would not hold.
    /// </summary>
    [Fact]
    public void GivenTwoSpellingsOfOneInstant_WhenComparingForSort_ThenTheResultIsZero()
    {
        // Arrange
        var withOffset = Temporal("2012-01-01T20:00:00+10:00", "dateTime");
        var withZulu = Temporal("2012-01-01T10:00:00Z", "dateTime");

        // Act
        var result = ValueOrdering.CompareForSort(withOffset, withZulu, "sort()");

        // Assert
        result.ShouldBe(0);
    }

    /// <summary>
    /// Transitivity, asserted as the property rather than as an ordering of one collection: <c>@2012-01</c>
    /// and <c>@2012-06</c> order determinately, and both are indeterminate against <c>@2012</c>, so an
    /// ordering that resolved indeterminacy per pair could and did place <c>@2012</c> on both sides of the
    /// same pair.
    /// </summary>
    [Fact]
    public void GivenAnIndeterminateTemporalTriple_WhenComparingForSort_ThenTheOrderIsTransitive()
    {
        // Arrange
        var year = Temporal("2012", "date");
        var january = Temporal("2012-01", "date");
        var june = Temporal("2012-06", "date");

        // Act
        var yearToJanuary = ValueOrdering.CompareForSort(year, january, "sort()");
        var januaryToJune = ValueOrdering.CompareForSort(january, june, "sort()");
        var yearToJune = ValueOrdering.CompareForSort(year, june, "sort()");

        // Assert
        yearToJanuary.ShouldBeLessThan(0);
        januaryToJune.ShouldBeLessThan(0);
        yearToJune.ShouldBeLessThan(0);
    }

    /// <summary>
    /// Keying quantities on the unit string is intransitive - <c>1 'g' == 1000 'mg'</c> while
    /// <c>'g' &lt; 'm' &lt; 'mg'</c> as text - so the bucket has to be the dimension, inside which every
    /// unit converts.
    /// </summary>
    [Fact]
    public void GivenQuantitiesWhoseUnitsOrderAgainstTheirValues_WhenComparingForSort_ThenTheOrderIsTransitive()
    {
        // Arrange
        var gram = Quantity(1m, "g");
        var metre = Quantity(1m, "m");
        var milligrams = Quantity(1000m, "mg");

        // Act
        var gramToMilligrams = ValueOrdering.CompareForSort(gram, milligrams, "sort()");
        var gramToMetre = ValueOrdering.CompareForSort(gram, metre, "sort()");
        var milligramsToMetre = ValueOrdering.CompareForSort(milligrams, metre, "sort()");

        // Assert
        gramToMilligrams.ShouldBe(0);
        Math.Sign(gramToMetre).ShouldBe(Math.Sign(milligramsToMetre));
    }

    [Fact]
    public void GivenABoolean_WhenComparing_ThenAnErrorNamingTheCallerIsSignalled()
    {
        // Arrange
        var left = Element(true, "boolean");
        var right = Element(false, "boolean");

        // Act
        Action compare = () => ValueOrdering.CompareValues(left, right, "min()");

        // Assert
        var error = Should.Throw<FhirPathEvaluationException>(compare);
        error.Message.ShouldBe("min() cannot order operands of type 'boolean' and 'boolean'.");
    }

    /// <summary>
    /// A FHIR decimal outside <see cref="decimal"/>'s range keeps its source text rather than losing the
    /// value, so both operands declare "decimal" and neither can be read as one. Naming the declared type
    /// alone said "cannot order operands of type 'decimal' and 'decimal'", which is true and tells the
    /// reader nothing about why.
    /// </summary>
    [Fact]
    public void GivenDecimalsThatArrivedAsTextAndCannotBeRead_WhenComparing_ThenTheErrorNamesTheRuntimeType()
    {
        // Arrange
        var left = Element("1e30", "decimal");
        var right = Element("2e30", "decimal");

        // Act
        Action compare = () => ValueOrdering.CompareValues(left, right, "sort()");

        // Assert
        var error = Should.Throw<FhirPathEvaluationException>(compare);
        error.Message.ShouldBe("sort() cannot order operands of type 'decimal (String)' and 'decimal (String)'.");
    }

    /// <summary>
    /// The declared type is also the gate that lets a decimal-as-text be read at all. Without it a String
    /// beside an Integer would quietly compare as a number, which FHIRPath makes an error.
    /// </summary>
    [Fact]
    public void GivenADecimalThatArrivedAsText_WhenComparingWithANumber_ThenItIsReadAsANumber()
    {
        // Arrange
        var text = Element("1.5", "decimal");
        var number = Element(2, "integer");

        // Act
        var result = ValueOrdering.CompareValues(text, number, "min()");

        // Assert
        result!.Value.ShouldBeLessThan(0);
    }

    [Fact]
    public void GivenAStringThatLooksNumeric_WhenComparingWithANumber_ThenAnErrorIsSignalled()
    {
        // Arrange
        var text = Element("1", "string");
        var number = Element(2, "integer");

        // Act
        Action compare = () => ValueOrdering.CompareValues(text, number, "min()");

        // Assert
        Should.Throw<FhirPathEvaluationException>(compare);
    }

    private static IElement Temporal(string literal, string instanceType) => Element(literal, instanceType);

    private static IElement Quantity(decimal value, string unit) => Element(new FhirQuantity(value, unit), "Quantity");

    private static IElement Element(object value, string instanceType) => new TestElement(value, instanceType);

    private sealed class TestElement(object? value, string instanceType) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType { get; } = instanceType;
        public object? Value { get; } = value;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => Value is not null;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
