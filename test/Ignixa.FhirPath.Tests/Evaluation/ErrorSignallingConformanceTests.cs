/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The cases where FHIRPath requires the engine to signal an error rather than return a value, and the
 * neighbouring cases where it requires empty rather than an error.
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
/// Covers the spec's "the evaluation will end and signal an error to the calling environment" clauses
/// across boolean logic, the Conversion section, the math operators and the unary operators.
/// </summary>
/// <remarks>
/// <para>
/// These clauses were being applied unevenly, and the unevenness was the defect: <c>(1|2).not()</c>
/// errored while <c>true and (1|2)</c> returned true, and <c>('a'|'b') &amp; 'c'</c> errored while
/// <c>(1|2).toInteger()</c> returned empty. Each rule is exercised here next to the empty-result rule it
/// is most easily confused with, because getting one right by breaking the other is the failure mode -
/// erroring on <c>{}.convertsToDate()</c>, say, or returning empty for <c>-(7.combine(3))</c>.
/// </para>
/// <para>
/// Firely agrees on the conversion, unary and concatenation groups. It does <i>not</i> agree on boolean
/// logic (it coerces a multi-item operand to true, including for the spec's own worked error example) or
/// on arithmetic overflow (it wraps silently under unchecked Int32 arithmetic). Those two are deliberate
/// divergences from Firely towards the spec, recorded here so they are not mistaken for regressions.
/// </para>
/// </remarks>
public class ErrorSignallingConformanceTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();

    /// <summary>
    /// The spec's own worked example of the singleton rule, on a real resource: "this expression will
    /// result in an error because of the multiple telecom elements".
    /// </summary>
    [Fact]
    public void GivenAPatientWithMultipleTelecoms_WhenAndingThemAsABooleanOperand_ThenAnErrorIsSignalled()
    {
        // Arrange
        var element = PatientWithTwoTelecoms();
        var expression = _parser.Parse("Patient.active and Patient.gender and Patient.telecom");

        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(
            () => _evaluator.Evaluate(element, expression, new EvaluationContext()).ToList());
    }

    /// <summary>
    /// The rewrite the spec recommends in the same paragraph must keep working - the point of the error is
    /// to push authors towards an explicit existence check, not to make repeating elements unusable.
    /// </summary>
    [Fact]
    public void GivenAPatientWithMultipleTelecoms_WhenTheExplicitCountFormIsUsed_ThenItEvaluatesWithoutError()
    {
        // Arrange
        var element = PatientWithTwoTelecoms();
        var expression = _parser.Parse("Patient.active and Patient.gender and Patient.telecom.count() = 2");

        // Act
        var result = _evaluator.Evaluate(element, expression, new EvaluationContext()).ToList();

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(true);
    }

    [Theory]
    [InlineData("(1 | 2).toInteger()")]
    [InlineData("(1 | 2).toLong()")]
    [InlineData("(1 | 2).toDecimal()")]
    [InlineData("(1 | 2).toBoolean()")]
    [InlineData("(1 | 2).toQuantity()")]
    [InlineData("('a' | 'b').toString()")]
    [InlineData("('2015' | '2016').toDate()")]
    [InlineData("('2015' | '2016').toDateTime()")]
    [InlineData("('14:00' | '15:00').toTime()")]
    [InlineData("(1 | 2).convertsToInteger()")]
    [InlineData("(1 | 2).convertsToLong()")]
    [InlineData("(1 | 2).convertsToDecimal()")]
    [InlineData("(1 | 2).convertsToBoolean()")]
    [InlineData("(1 | 2).convertsToQuantity()")]
    [InlineData("('a' | 'b').convertsToString()")]
    [InlineData("('2015' | '2016').convertsToDate()")]
    [InlineData("('2015' | '2016').convertsToDateTime()")]
    [InlineData("('14:00' | '15:00').convertsToTime()")]
    public void GivenAMultiItemCollection_WhenConverting_ThenAnErrorIsSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// The other half of the Conversion arity rule: empty in, empty out, for both families. This is what
    /// stops the multi-item error above being implemented as a blanket "not exactly one item" throw.
    /// </summary>
    [Theory]
    [InlineData("{}.toInteger()")]
    [InlineData("{}.toDecimal()")]
    [InlineData("{}.toBoolean()")]
    [InlineData("{}.toString()")]
    [InlineData("{}.toDate()")]
    [InlineData("{}.toTime()")]
    [InlineData("{}.toQuantity()")]
    [InlineData("{}.convertsToInteger()")]
    [InlineData("{}.convertsToLong()")]
    [InlineData("{}.convertsToDecimal()")]
    [InlineData("{}.convertsToBoolean()")]
    [InlineData("{}.convertsToString()")]
    [InlineData("{}.convertsToDate()")]
    [InlineData("{}.convertsToDateTime()")]
    [InlineData("{}.convertsToTime()")]
    [InlineData("{}.convertsToQuantity()")]
    public void GivenAnEmptyCollection_WhenConverting_ThenTheResultIsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// Non-empty but unconvertible input still answers false, which is what distinguishes it from the
    /// empty case above. Official <c>testIntegerLiteralConvertsToBooleanFalse</c> asserts the first row.
    /// </summary>
    [Theory]
    [InlineData("2.convertsToBoolean()", false)]
    [InlineData("'a'.convertsToInteger()", false)]
    [InlineData("'1.a'.convertsToDecimal()", false)]
    public void GivenUnconvertibleInput_WhenTestingConvertibility_ThenTheResultIsFalse(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// "the item is a DateTime, in which case the year, month, and day components are extracted directly
    /// without timezone conversion/normalization" - the spec's own example returns the local calendar
    /// date, not the UTC one the offset would shift it to.
    /// </summary>
    [Fact]
    public void GivenADateTime_WhenToDate_ThenTheDateComponentsAreExtractedWithoutTimezoneShift()
    {
        // Act
        var result = Evaluate("@2024-01-15T23:30:00-05:00.toDate()");

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe("2024-01-15");
        result[0].InstanceType.ShouldBe("date");
    }

    [Fact]
    public void GivenADateTime_WhenConvertsToDate_ThenTheResultIsTrue()
    {
        // Act
        var result = Evaluate("@2024-01-15T23:30:00-05:00.convertsToDate()");

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(true);
    }

    /// <summary>
    /// A String is held to the default <c>yyyy-MM-DD</c> format, so the same lexical form that a DateTime
    /// gets truncated from is empty when it arrives as a String. Truncating on the presence of a 'T'
    /// rather than on the item's type would lose this distinction.
    /// </summary>
    [Fact]
    public void GivenAStringHoldingADateTime_WhenToDate_ThenTheResultIsEmpty()
    {
        // Act
        var result = Evaluate("'2024-01-15T23:30:00-05:00'.toDate()");

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("'true'.toBoolean()", true)]
    [InlineData("'t'.toBoolean()", true)]
    [InlineData("'T'.toBoolean()", true)]
    [InlineData("'yes'.toBoolean()", true)]
    [InlineData("'y'.toBoolean()", true)]
    [InlineData("'1'.toBoolean()", true)]
    [InlineData("'1.0'.toBoolean()", true)]
    [InlineData("'false'.toBoolean()", false)]
    [InlineData("'f'.toBoolean()", false)]
    [InlineData("'no'.toBoolean()", false)]
    [InlineData("'n'.toBoolean()", false)]
    [InlineData("'0'.toBoolean()", false)]
    [InlineData("'0.0'.toBoolean()", false)]
    [InlineData("'YES'.convertsToBoolean()", true)]
    public void GivenAStringInTheBooleanRepresentationTable_WhenToBoolean_ThenItConverts(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    [Fact]
    public void GivenAStringOutsideTheBooleanRepresentationTable_WhenToBoolean_ThenTheResultIsEmpty()
    {
        // Act
        var result = Evaluate("'hello'.toBoolean()");

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// "the item is an Integer or Long", worked as <c>42L.toInteger() // 42</c>. Note this engine parses
    /// the <c>L</c> suffix that Firely 6.0.1's grammar rejects outright, so the case is reachable here.
    /// </summary>
    [Fact]
    public void GivenALong_WhenToInteger_ThenItConverts()
    {
        // Act
        var result = Evaluate("42L.toInteger()");

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(42);
        result[0].InstanceType.ShouldBe("integer");
    }

    /// <summary>
    /// A Long past Integer's range has no Integer to return, so it is empty rather than a wrapped value.
    /// </summary>
    [Fact]
    public void GivenALongBeyondIntegerRange_WhenToInteger_ThenTheResultIsEmpty()
    {
        // Act
        var result = Evaluate("9999999999L.toInteger()");

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// The spec pins the accepted string forms by regex and format string; the previous
    /// <see cref="System.Globalization.NumberStyles"/> and <see cref="TimeSpan"/> parses were both wider.
    /// </summary>
    [Theory]
    [InlineData("'1,000'.toDecimal()")]
    [InlineData("'1e3'.toDecimal()")]
    [InlineData("'25:00'.toTime()")]
    [InlineData("'1.02:03'.toTime()")]
    public void GivenAStringOutsideTheSpecFormat_WhenConverting_ThenTheResultIsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// The forms the official suite asserts must keep converting, including the bare hour of
    /// <c>testStringHourConvertsToTime</c>.
    /// </summary>
    [Theory]
    [InlineData("'1.1'.toDecimal()")]
    [InlineData("'-42'.toDecimal()")]
    [InlineData("'14'.toTime()")]
    [InlineData("'14:34'.toTime()")]
    [InlineData("'14:34:28'.toTime()")]
    [InlineData("'14:34:28.123'.toTime()")]
    public void GivenAStringInTheSpecFormat_WhenConverting_ThenItConverts(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
    }

    /// <summary>
    /// §Math: "Operations that cause arithmetic overflow or underflow will result in empty ({ })."
    /// </summary>
    /// <remarks>
    /// The narrowing cast back to Integer is where these land, and .NET checks decimal-to-integral
    /// conversions whatever the compilation context - so before this these escaped as a raw
    /// <see cref="OverflowException"/>, an exception type from outside the engine's own error surface.
    /// Firely wraps instead, answering <c>-2147483648</c> for the first row; empty is what the spec says.
    /// </remarks>
    [Theory]
    [InlineData("2147483647 + 1")]
    [InlineData("2147483647 * 2")]
    [InlineData("(-2147483647 - 1) - 1")]
    [InlineData("2147483647 div 1 * 3")]
    public void GivenAnArithmeticOverflow_WhenEvaluating_ThenTheResultIsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// Overflow becoming empty must not swallow the math preamble's incompatible-operand error, which is
    /// a different clause reached through the same operators.
    /// </summary>
    [Theory]
    [InlineData("'a' - 'b'")]
    [InlineData("@1974-12-25 + 7")]
    public void GivenIncompatibleOperands_WhenEvaluatingArithmetic_ThenAnErrorIsStillSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// "The unary operators support a single item input operand of type Integer, Long, Decimal, or
    /// Quantity", with the precedence section annotating <c>-(7.combine(3))</c> as <c>// ERROR</c>
    /// "because unary negation cannot be applied to a list".
    /// </summary>
    [Theory]
    [InlineData("-(7.combine(3))")]
    [InlineData("+(7.combine(3))")]
    [InlineData("-(1 | 2)")]
    [InlineData("+true")]
    [InlineData("+'abc'")]
    [InlineData("-'abc'")]
    public void GivenAnIncompatibleUnaryOperand_WhenEvaluating_ThenAnErrorIsSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// "If the input collection is empty, the result is empty ({ })" - the one arm of the unary clause
    /// that is not an error.
    /// </summary>
    [Theory]
    [InlineData("-{}")]
    [InlineData("+{}")]
    public void GivenAnEmptyUnaryOperand_WhenEvaluating_ThenTheResultIsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("+5", 5)]
    [InlineData("-(0 - 5)", 5)]
    public void GivenANumericUnaryOperand_WhenEvaluating_ThenItStillEvaluates(string expression, int expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// <c>&amp;</c> is a §Math subsection defined "For strings", and Integer-to-String is an explicit
    /// conversion, so a non-String operand is one of the preamble's "incompatible items".
    /// </summary>
    [Theory]
    [InlineData("1 & 'a'")]
    [InlineData("'a' & 1")]
    [InlineData("true & 'a'")]
    [InlineData("@2024-01-15 & 'x'")]
    [InlineData("('a' | 'b') & 'c'")]
    public void GivenANonStringOperand_WhenConcatenating_ThenAnErrorIsSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// The spec's own worked example of the empty-as-empty-string rule, which the type check must not
    /// reinterpret as an incompatible operand.
    /// </summary>
    [Theory]
    [InlineData("'ABC' & {} & 'DEF'", "ABCDEF")]
    [InlineData("'1' & {}", "1")]
    [InlineData("{} & 'b'", "b")]
    public void GivenAnEmptyOperand_WhenConcatenating_ThenItIsTreatedAsTheEmptyString(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    private List<IElement> Evaluate(string expression)
    {
        var parsed = _parser.Parse(expression);
        return _evaluator.Evaluate(new ScalarRoot(), parsed).ToList();
    }

    private IElement PatientWithTwoTelecoms()
    {
        const string Json = """
        {
          "resourceType": "Patient",
          "id": "pat1",
          "active": true,
          "gender": "male",
          "telecom": [
            { "system": "phone", "value": "555-1111" },
            { "system": "email", "value": "a@b.example" }
          ]
        }
        """;

        return ResourceJsonNode.Parse(Json).ToElement(_r4Provider);
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
