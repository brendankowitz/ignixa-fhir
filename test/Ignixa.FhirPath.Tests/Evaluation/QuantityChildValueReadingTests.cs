/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * How a Quantity's value child is read off an element, for the CLR types a schema-aware read does not
 * produce but other IElement implementations do.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers the single read of a resource-backed Quantity's <c>value</c> child that <c>=</c>, <c>~</c>,
/// <c>&lt;</c>, <c>sort()</c>, <c>distinct()</c>, <c>in</c> and the aggregates all share.
/// </summary>
/// <remarks>
/// <para>
/// There were two copies of this read. The one deleted from <c>FhirPathEvaluator</c> carried
/// <see cref="CultureInfo.InvariantCulture"/> on its string branch; the surviving one in
/// <c>QuantityEvaluator</c> did not, and the de-duplication then routed every operator listed above
/// onto it. That is a widening of blast radius, not a narrowing: a defect that used to affect one
/// comparison path now affects all of them.
/// </para>
/// <para>
/// The elements here are hand-built because <c>SchemaAwareElement</c> cannot produce these CLR types -
/// it parses a <c>decimal</c> under the invariant culture itself and keeps the source text when that
/// fails. <see cref="IElement"/> is an interface with several implementations in this repo (the Firely
/// SDK adapter and the mapping-language context among them) and <c>ExtractQuantityFromChildren</c>
/// accepts any of them, so the branches under test are reachable and untested rather than dead. What is
/// deliberately not claimed is that the server's own JSON reader reaches them.
/// </para>
/// </remarks>
public class QuantityChildValueReadingTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// A comma-decimal host reads <c>"1.5"</c> as fifteen under the default number styles, because
    /// <c>'.'</c> is that culture's group separator and <c>"1.5"</c> is a well-formed integer under its
    /// rules. Nothing throws and nothing logs; the quantity is simply ten times too big.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void GivenAQuantityValueHeldAsText_WhenTheHostCultureVaries_ThenItsMagnitudeIsUnchanged(string cultureName)
    {
        // Arrange
        var subject = Quantity("1.5", "mg");

        // Act
        var result = UnderCulture(cultureName, () => Evaluate(subject, "$this.sum()"));

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBeOfType<FhirQuantity>().Value.ShouldBe(1.5m);
    }

    /// <summary>
    /// The group separator is the other half of the same defect: a culture that writes thousands with
    /// <c>'.'</c> must not read <c>"1.500"</c> as fifteen hundred, and the FHIR wire format never carries
    /// a group separator at all.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void GivenAQuantityValueWithATrailingFraction_WhenTheHostCultureVaries_ThenItIsNotReadAsThousands(
        string cultureName)
    {
        // Arrange
        var subject = Quantity("1.500", "mg");

        // Act
        var result = UnderCulture(cultureName, () => Evaluate(subject, "$this.sum()"));

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBeOfType<FhirQuantity>().Value.ShouldBe(1.500m);
    }

    /// <summary>
    /// A value no <see cref="decimal"/> can hold is FHIRPath's arithmetic overflow, which §Math answers
    /// with empty. The read cast to <see cref="decimal"/> unguarded, so it threw
    /// <see cref="OverflowException"/> from inside the operand screen - outside the aggregates' own
    /// overflow <c>catch</c> - and a conformant resource was rejected rather than answered.
    /// </summary>
    [Theory]
    [InlineData(1e30)]
    [InlineData(-1e30)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GivenAQuantityValueDecimalCannotHold_WhenSummed_ThenItAnswersEmptyRatherThanThrowing(double value)
    {
        // Arrange
        var subject = Quantity(value, "mg");

        // Act
        var result = Evaluate(subject, "$this.sum()");

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// The same operand reached through the comparison operators, which is the wider blast radius the
    /// de-duplication created: <c>=</c>, <c>~</c> and <c>&lt;</c> did not go through this read before the
    /// two copies were merged, and every one of them inherited the unguarded cast.
    /// </summary>
    /// <remarks>
    /// The assertion is specifically that no <see cref="OverflowException"/> escapes, which is the whole
    /// of what the narrowing fixes. It is deliberately not "the operator answers": <c>&lt;</c> reports an
    /// operand it cannot read as a quantity as a domain error rather than as §Math's empty, which is a
    /// separate question about the comparison path and is left as it was found. Asserting the weaker
    /// property here keeps this test about the read.
    /// </remarks>
    [Theory]
    [InlineData("$this < 10 'g'")]
    [InlineData("$this = 10 'g'")]
    [InlineData("$this ~ 10 'g'")]
    public void GivenAQuantityValueDecimalCannotHold_WhenCompared_ThenNoOverflowEscapes(string expression)
    {
        // Arrange
        var subject = Quantity(double.PositiveInfinity, "mg");

        // Act
        var thrown = Record.Exception(() => Evaluate(subject, expression));

        // Assert
        thrown.ShouldNotBeOfType<OverflowException>();
    }

    /// <summary>
    /// Equality and equivalence do answer, rather than merely not throwing, and neither of them calls an
    /// unreadable operand equal to a real quantity.
    /// </summary>
    /// <remarks>
    /// Both report "not equal" rather than <c>=</c> reporting empty. That is the pre-existing collapse in
    /// the equality path, not something this narrowing chose, and it is asserted as "not true" rather
    /// than pinned to either spelling so that this test fails on a wrong answer and not on a change of
    /// mind about the undecided case.
    /// </remarks>
    [Theory]
    [InlineData("$this = 10 'g'")]
    [InlineData("$this ~ 10 'g'")]
    public void GivenAQuantityValueDecimalCannotHold_WhenTestedForEquality_ThenItIsNotReportedEqual(string expression)
    {
        // Arrange
        var subject = Quantity(double.PositiveInfinity, "mg");

        // Act
        var result = Evaluate(subject, expression);

        // Assert
        result.ShouldNotContain(element => Equals(element.Value, true));
    }

    /// <summary>
    /// Guard: narrowing must not have refused the values it can hold. A <see cref="double"/> inside
    /// <see cref="decimal"/>'s range still reads.
    /// </summary>
    [Fact]
    public void GivenAQuantityValueHeldAsADoubleInRange_WhenSummed_ThenItIsRead()
    {
        // Arrange
        var subject = Quantity(2.5d, "mg");

        // Act
        var result = Evaluate(subject, "$this.sum()");

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBeOfType<FhirQuantity>().Value.ShouldBe(2.5m);
    }

    private List<IElement> Evaluate(IElement subject, string expression)
    {
        return _evaluator.Evaluate(subject, _parser.Parse(expression)).ToList();
    }

    private static T UnderCulture<T>(string cultureName, Func<T> act)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static IElement Quantity(object value, string code) => new ComplexElement(
        "value",
        "Quantity",
        [new PrimitiveChild("value", "decimal", value), new PrimitiveChild("code", "code", code)]);

    private sealed class ComplexElement(string name, string instanceType, IReadOnlyList<IElement> children) : IElement
    {
        public string Name => name;
        public string InstanceType => instanceType;
        public object? Value => null;
        public string Location => name;
        public IType? Type => null;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? childName = null) =>
            childName is null ? children : children.Where(child => child.Name == childName).ToList();

        public T? Meta<T>() where T : class => null;
    }

    private sealed class PrimitiveChild(string name, string instanceType, object value) : IElement
    {
        public string Name => name;
        public string InstanceType => instanceType;
        public object Value => value;
        public string Location => name;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? childName = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
