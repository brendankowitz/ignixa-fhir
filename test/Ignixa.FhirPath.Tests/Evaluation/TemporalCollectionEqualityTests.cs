/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The collection functions and the equality operator must answer the same question about temporals.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Holds every collection operation that uses equality to the answer the <c>=</c> operator gives.
/// </summary>
/// <remarks>
/// <para>
/// There were three temporal equality implementations. <c>=</c> resolved both operands to
/// <c>FhirTemporal</c> and compared instants. <c>FunctionHelpers.AreEqual</c> routed both through
/// <c>WireValue.AsWireString</c>, i.e. <c>FhirTemporal.Literal</c>, giving a literal string compare, and
/// backed <c>|</c>, <c>in</c>, <c>contains</c>, <c>intersect</c> and <c>exclude</c>. <c>distinct()</c>
/// used a third - an <c>IEqualityComparer</c> over <c>Value.GetHashCode()</c> and <c>Value.Equals</c>,
/// which for two equal temporals with different literals hashed differently and so never even called the
/// comparison.
/// </para>
/// <para>
/// So <c>@2012-01-01T10:00:00Z = @2012-01-01T20:00:00+10:00</c> was <see langword="true"/> as an operator
/// and <see langword="false"/> as membership. All three now call <c>TemporalOperand.AreEqual</c>.
/// </para>
/// <para>
/// That helper is built on <c>FhirTemporal.Compare</c> rather than <c>FhirTemporal.Equals</c>, and the
/// choice is load-bearing rather than incidental. <c>FhirTemporal.Equals</c> is identity: it folds
/// <c>Precision</c> and <c>HasTimezone</c> into its key so that hashed and sorted collections behave, and
/// its own remarks say so. FHIRPath equality is not identity - milliseconds are the fractional part of
/// the second tier rather than a tier of their own, so <c>@2012-01-01T10:00:30</c> and
/// <c>@2012-01-01T10:00:30.000</c> are the same value while <c>FhirTemporal.Equals</c> calls them
/// different. Choosing <c>Equals</c> would therefore have replaced the timezone disagreement with a
/// millisecond one; <see cref="GivenTwoTemporalsDifferingOnlyInMillisecondPrecision_WhenDeduplicated_ThenTheyCollapse"/>
/// is the test that would have caught it. <c>Compare</c>'s third state (indeterminate) collapses to "not
/// the same item" for membership, because membership asserts that an equal item is present and an
/// indeterminate comparison is not that assertion.
/// </para>
/// </remarks>
public class TemporalCollectionEqualityTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// The same instant written in two timezones. Every surface must agree that these are one value.
    /// </summary>
    [Theory]
    [InlineData("@2012-01-01T10:00:00Z = @2012-01-01T20:00:00+10:00", "true")]
    [InlineData("@2012-01-01T10:00:00Z != @2012-01-01T20:00:00+10:00", "false")]
    [InlineData("(@2012-01-01T10:00:00Z | @2012-01-01T20:00:00+10:00).count()", "1")]
    [InlineData("(@2012-01-01T10:00:00Z | @2012-01-01T20:00:00+10:00).distinct().count()", "1")]
    [InlineData("@2012-01-01T10:00:00Z in (@2012-01-01T20:00:00+10:00)", "true")]
    [InlineData("(@2012-01-01T20:00:00+10:00) contains @2012-01-01T10:00:00Z", "true")]
    [InlineData("(@2012-01-01T10:00:00Z).intersect(@2012-01-01T20:00:00+10:00).count()", "1")]
    [InlineData("(@2012-01-01T10:00:00Z).exclude(@2012-01-01T20:00:00+10:00).count()", "0")]
    [InlineData("(@2012-01-01T10:00:00Z).combine(@2012-01-01T20:00:00+10:00).distinct().count()", "1")]
    [InlineData("(@2012-01-01T10:00:00Z).combine(@2012-01-01T20:00:00+10:00).isDistinct()", "false")]
    public void GivenTheSameInstantInTwoTimezones_WhenComparedByAnySurface_ThenItIsOneValue(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// The case that rules out <c>FhirTemporal.Equals</c> as the membership comparison: FHIRPath treats
    /// seconds and milliseconds as one precision tier, so these are the same value and the operator
    /// already said so.
    /// </summary>
    [Theory]
    [InlineData("@2012-01-01T10:00:30 = @2012-01-01T10:00:30.000", "true")]
    [InlineData("(@2012-01-01T10:00:30 | @2012-01-01T10:00:30.000).distinct().count()", "1")]
    [InlineData("(@2012-01-01T10:00:30).combine(@2012-01-01T10:00:30.000).distinct().count()", "1")]
    [InlineData("@T10:00:30 = @T10:00:30.000", "true")]
    [InlineData("(@T10:00:30 | @T10:00:30.000).distinct().count()", "1")]
    [InlineData("(@T10:00:30).combine(@T10:00:30.000).isDistinct()", "false")]
    public void GivenTwoTemporalsDifferingOnlyInMillisecondPrecision_WhenDeduplicated_ThenTheyCollapse(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// Where the operator declines to decide, membership must say "not the same item" rather than
    /// deduplicate. Deduplicating would silently discard a value the engine refuses to call equal.
    /// </summary>
    [Theory]
    [InlineData("@2012 = @2012-01", 0)]
    [InlineData("@2012-01-01T10:00:00Z = @2012-01-01T10:00:00", 0)]
    [InlineData("@T10:00:00 = @T10:00", 0)]
    [InlineData("@2012-01-01 = @2012-01-01T00:00:00Z", 0)]
    public void GivenAnIndeterminateComparison_WhenUsingTheOperator_ThenItYieldsEmpty(string expression, int expectedCount)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(expectedCount);
    }

    [Theory]
    [InlineData("(@2012 | @2012-01).distinct().count()", "2")]
    [InlineData("(@2012-01-01T10:00:00Z | @2012-01-01T10:00:00).distinct().count()", "2")]
    [InlineData("(@T10:00:00 | @T10:00).distinct().count()", "2")]
    [InlineData("(@2012-01-01 | @2012-01-01T00:00:00Z).distinct().count()", "2")]
    [InlineData("(@2012-01-01T10:00:30.5 | @2012-01-01T10:00:30).distinct().count()", "2")]
    [InlineData("(@2012).combine(@2012-01).distinct().count()", "2")]
    [InlineData("(@2012-01-01T10:00:00Z).combine(@2012-01-01T10:00:00).isDistinct()", "true")]
    [InlineData("@2012 in (@2012-01)", "false")]
    public void GivenAnIndeterminateComparison_WhenUsedForMembership_ThenTheItemsStayDistinct(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// Identical values must still deduplicate - the fix must not make every temporal distinct.
    /// </summary>
    [Theory]
    [InlineData("(@2012-01-01 | @2012-01-01).distinct().count()", "1")]
    [InlineData("(@2012-01-01T10:00:00Z | @2012-01-01T10:00:00Z).count()", "1")]
    [InlineData("(@T10:30:00 | @T10:30:00).distinct().count()", "1")]
    [InlineData("@2012-01-01 in (@2012-01-01)", "true")]
    public void GivenIdenticalTemporals_WhenDeduplicated_ThenTheyCollapse(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// A typed <c>FhirTemporal</c> read from a resource and a FHIRPath literal are the two
    /// representations the old code inspected separately. They must meet in the middle.
    /// </summary>
    [Theory]
    [InlineData("birthDate in (@1974-12-25)", "true")]
    [InlineData("(birthDate | @1974-12-25).count()", "1")]
    [InlineData("(birthDate).intersect(@1974-12-25).count()", "1")]
    [InlineData("(birthDate).exclude(@1974-12-25).count()", "0")]
    [InlineData("(issued | @2024-06-15T08:00:00Z).distinct().count()", "1")]
    [InlineData("(birthTime | @T10:30:00).distinct().count()", "1")]
    [InlineData("(birthDate | @1974-12-26).count()", "2")]
    public void GivenATypedTemporalAndALiteral_WhenComparedByCollectionFunctions_ThenTheyMatch(string expression, string expected)
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();
        var parsed = _parser.Parse(expression);

        // Act
        var result = _evaluator.Evaluate(subject, parsed, DifferentialFixture.CreateContext(subject)).ToList();

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// Non-temporal equality must be unaffected, including the numeric coercion <c>distinct()</c>'s old
    /// hash-based comparer could not see.
    /// </summary>
    [Theory]
    [InlineData("('a' | 'a').distinct().count()", "1")]
    [InlineData("('a' | 'b').distinct().count()", "2")]
    [InlineData("(1 | 1.0).distinct().count()", "1")]
    [InlineData("(1 | 2).distinct().count()", "2")]
    [InlineData("(true | true).distinct().count()", "1")]
    [InlineData("'a'.combine('a').isDistinct()", "false")]
    [InlineData("'a'.combine('b').isDistinct()", "true")]
    [InlineData("1.combine(1.0).isDistinct()", "false")]
    public void GivenNonTemporalValues_WhenDeduplicated_ThenEqualityIsUnchanged(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

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
