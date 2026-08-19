/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * sort()'s ordering rule for the types its comparer used to compare as text.
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
/// Covers <c>sort()</c> over quantities, temporals and mixed numerics.
/// </summary>
/// <remarks>
/// <para>
/// The comparer behind <c>sort()</c> dispatched on the non-generic <see cref="IComparable"/>.
/// <see cref="FhirTemporal"/> deliberately does not implement it and <see cref="FhirQuantity"/> implements
/// no ordering at all, so both fell through to an ordinal compare of <c>ToString()</c>: <c>10 'mg'</c>
/// sorted before <c>9 'mg'</c>, and two spellings of one instant sorted apart. A bare <c>catch</c> around
/// <c>CompareTo</c> then reported "equal" whenever a cross-type comparison threw, so genuinely unrelated
/// types interleaved silently.
/// </para>
/// <para>
/// Every case below is written so that the old text ordering gives a different answer from the new value
/// ordering - otherwise the test asserts nothing about the fix.
/// </para>
/// </remarks>
public class SortOrderingTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "id": "p1",
      "birthDate": "1974-12-25",
      "deceasedDateTime": "2020-03-04T10:00:00+00:00"
    }
    """;

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// The headline case: <c>"10 'mg'"</c> is ordinally less than <c>"9 'mg'"</c>, so the text fallback
    /// inverted the only ordering a quantity has.
    /// </summary>
    [Fact]
    public void GivenQuantitiesInTheSameUnit_WhenSorting_ThenTheyOrderNumerically()
    {
        // Arrange
        var expression = "(9 'mg' | 10 'mg').sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["9 'mg'", "10 'mg'"]);
    }

    /// <summary>
    /// Ordering has to run on converted values, not on the literals, or the unit is decorative.
    /// </summary>
    [Fact]
    public void GivenQuantitiesInCompatibleUnits_WhenSorting_ThenTheyOrderByConvertedValue()
    {
        // Arrange
        var expression = "(1 'm').combine(50 'cm').sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["50 'cm'", "1 'm'"]);
    }

    /// <summary>
    /// Incompatible units are FHIRPath's own empty result. sort() has nowhere to put an empty comparison,
    /// so it leaves the pair alone - it must not invent an order and must not raise an error either.
    /// </summary>
    [Fact]
    public void GivenQuantitiesInIncompatibleUnits_WhenSorting_ThenTheOrderIsUnchanged()
    {
        // Arrange
        var expression = "(1 'mg').combine(1 'm').sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["1 'mg'", "1 'm'"]);
    }

    /// <summary>
    /// Two spellings of one instant are equal, so a stable sort leaves them as they arrived.
    /// </summary>
    [Fact]
    public void GivenTwoSpellingsOfTheSameInstant_WhenSorting_ThenTheOrderIsUnchanged()
    {
        // Arrange
        var expression = "(@2012-01-01T20:00:00+10:00).combine(@2012-01-01T10:00:00Z).sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2012-01-01T20:00:00+10:00", "2012-01-01T10:00:00Z"]);
    }

    /// <summary>
    /// The proof that offsets are normalised rather than compared as text: the earlier instant is the one
    /// whose literal is ordinally larger, so text ordering and instant ordering disagree.
    /// </summary>
    [Fact]
    public void GivenInstantsWrittenInDifferentOffsets_WhenSorting_ThenTheyOrderByUtc()
    {
        // Arrange
        // @2012-01-01T09:00:00+10:00 is 2011-12-31T23:00:00Z, an hour before @2012-01-01T01:00:00Z, but
        // its literal sorts after under any ordinal compare.
        var expression = "(@2012-01-01T01:00:00Z).combine(@2012-01-01T09:00:00+10:00).sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2012-01-01T09:00:00+10:00", "2012-01-01T01:00:00Z"]);
    }

    /// <summary>
    /// A floating local time could sit at any offset, so it overlaps a fixed instant rather than ordering
    /// against it. FHIRPath calls that empty; sort() leaves the pair alone.
    /// </summary>
    [Fact]
    public void GivenAFloatingAndATimezoneBearingDateTime_WhenSorting_ThenTheOrderIsUnchanged()
    {
        // Arrange
        var expression = "(@2012-01-01T10:00:00Z).combine(@2012-01-01T09:00:00).sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2012-01-01T10:00:00Z", "2012-01-01T09:00:00"]);
    }

    /// <summary>
    /// <c>@2012</c> spans the whole of 2012 and <c>@2012-01</c> sits inside it, so neither precedes the
    /// other. Ordinally <c>"2012-01" &gt; "2012"</c>, which is the reorder this case pins shut.
    /// </summary>
    [Fact]
    public void GivenTemporalsOfDifferentPrecision_WhenSorting_ThenTheOrderIsUnchanged()
    {
        // Arrange
        var expression = "(@2012-01).combine(@2012).sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2012-01", "2012"]);
    }

    /// <summary>
    /// A resource supplies a typed <see cref="FhirTemporal"/> while an <c>@</c>-literal is still a raw
    /// string, so one collection holds both representations. They have to reconcile rather than land in
    /// the mixed-type error path.
    /// </summary>
    [Fact]
    public void GivenAResourceBackedTemporalAndALiteral_WhenSorting_ThenTheyOrderByInstant()
    {
        // Arrange
        // The literal is 2020-03-04T09:00:00Z, an hour before the resource's value, but its literal is
        // ordinally larger.
        var patient = Parse(PatientJson);

        // Act
        var result = patient
            .Select("Patient.deceasedDateTime.combine(@2020-03-04T20:00:00+11:00).sort()")
            .Select(e => e.Value?.ToString())
            .ToList();

        // Assert
        result.ShouldBe(["2020-03-04T20:00:00+11:00", "2020-03-04T10:00:00+00:00"]);
    }

    /// <summary>
    /// Sorting a string against a number silently interleaved them as equal. An error is the answer
    /// FHIRPath's Comparison section gives for differing types, and the only one a server can act on.
    /// </summary>
    [Fact]
    public void GivenAStringAndANumber_WhenSorting_ThenAnErrorNamingBothTypesIsSignalled()
    {
        // Arrange
        var expression = "('a').combine(1).sort()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        var error = Should.Throw<FhirPathEvaluationException>(evaluate);
        error.Message.ShouldContain("string");
        error.Message.ShouldContain("integer");
    }

    /// <summary>
    /// FHIRPath defines the ordering operators for String, Integer, Decimal, Quantity, Date, DateTime and
    /// Time. Boolean is not among them, and it is an <see cref="IComparable"/> that would otherwise order
    /// itself without anyone noticing.
    /// </summary>
    [Fact]
    public void GivenBooleans_WhenSorting_ThenAnErrorIsSignalled()
    {
        // Arrange
        var expression = "(true).combine(false).sort()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    /// <summary>
    /// Integer and Decimal are one numeric ordering, not two. <c>int.CompareTo(decimal)</c> throws, which
    /// the old comparer swallowed into "equal", so a mixed collection came back unsorted.
    /// </summary>
    [Fact]
    public void GivenIntegersAndDecimalsTogether_WhenSorting_ThenTheyOrderNumerically()
    {
        // Arrange
        var expression = "(3).combine(1.5).combine(2).sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["1.5", "2", "3"]);
    }

    /// <summary>
    /// The same rule at its boundary: <c>1</c> and <c>1.0</c> are the same number, so neither precedes the
    /// other and a stable sort leaves them put.
    /// </summary>
    [Fact]
    public void GivenAnIntegerAndAnEquivalentDecimal_WhenSorting_ThenNeitherPrecedesTheOther()
    {
        // Arrange
        var expression = "(1.0).combine(1).sort()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["1.0", "1"]);
    }

    private static IElement Parse(string json) => ResourceJsonNode.Parse(json).ToElement(Schema);

    private List<string?> Render(string expression) =>
        Evaluate(expression).Select(e => e.Value?.ToString()).ToList();

    private List<IElement> Evaluate(string expression)
    {
        var parsed = _parser.Parse(expression);
        return _evaluator.Evaluate(new ScalarRoot(), parsed).ToList();
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
