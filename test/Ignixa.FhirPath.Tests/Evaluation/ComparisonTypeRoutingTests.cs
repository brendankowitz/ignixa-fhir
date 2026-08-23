/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins that comparison, equality, equivalence and the boundary functions decide "is this a temporal?"
 * from the operand's instance type, never from the shape of its value.
 *
 * The two are separable only because a FHIRPath string literal keeps every character it was written
 * with while a temporal literal loses its sigil: 'X2013' with an at-sign arrives as the CLR string
 * "@2013" carrying instance type string, and @2013 arrives as "2013" carrying instance type date. A
 * predicate over the value therefore reads the String as the temporal and the Date as ordinary text,
 * which is exactly backwards.
 *
 * Authorities, recorded because the spec and the reference implementation split the work:
 *  - Comparison is decided by FHIRPath 3.0 §Comparison: "Both arguments must be of the same type (or
 *    implicitly convertible to the same type), and the evaluator will throw an error if the types
 *    differ." The implicit-conversion table lists String-to-Date as Explicit, so no conversion applies.
 *  - Equality and equivalence are decided by HAPI, because the spec states the same-type requirement
 *    without stating the outcome when it is violated. HAPI's FHIRPathEngine.doEquals and doEquivalent
 *    gate their temporal branch on hasType(...) and otherwise compare primitiveValue() as text, which
 *    makes both false here rather than empty.
 *  - lowBoundary()/highBoundary() are decided by §lowBoundary: "The function can only be used with
 *    Decimal, Date, DateTime, and Time values."
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class ComparisonTypeRoutingTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("'@2013' = @2013", false)]
    [InlineData("'@2013' != @2013", true)]
    [InlineData("'@2013' ~ @2013", false)]
    [InlineData("'@2013' !~ @2013", true)]
    public void GivenAStringLiteralSpellingATemporal_WhenComparedToATemporalLiteral_ThenTheyAreNotEqual(
        string expression, bool expected)
    {
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("'@2013' < @2013-01")]
    [InlineData("'@2013' > @2013-01")]
    [InlineData("'@2013' <= @2013")]
    [InlineData("'@2013' >= @2013")]
    public void GivenAStringLiteralSpellingATemporal_WhenOrderedAgainstATemporalLiteral_ThenItIsATypeError(
        string expression)
    {
        var thrown = Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression).ToList());

        thrown.Message.ShouldContain("must be of the same type");
    }

    [Fact]
    public void GivenAResourceBackedDate_WhenOrderedAgainstAStringLiteral_ThenItIsATypeError()
    {
        // The real-world shape of the same defect: before the fix this answered a definite false,
        // because the String operand sniffed as a date and the two were compared as instants.
        var thrown = Should.Throw<FhirPathEvaluationException>(
            () => Evaluate("$this < '1980-01-01'").ToList());

        thrown.Message.ShouldContain("must be of the same type");
    }

    [Theory]
    [InlineData("'@2013'.lowBoundary()")]
    [InlineData("'@2013'.highBoundary()")]
    [InlineData("'@T12:00'.lowBoundary()")]
    [InlineData("'2013'.lowBoundary()")]
    public void GivenAStringLiteralSpellingATemporal_WhenTakingABoundary_ThenItIsEmpty(string expression)
    {
        Evaluate(expression).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("('@2013' | @2013).count()", 2)]
    [InlineData("('@2013' | @2013).distinct().count()", 2)]
    public void GivenAStringAndATemporalSpellingTheSameText_WhenCombined_ThenNeitherDedupesTheOther(
        string expression, int expected)
    {
        // Collection membership answers the same question the = operator does, through
        // FunctionHelpers.AreElementsEqual. Before the fix its untyped fallback stripped a leading
        // sigil from either operand, so a String and a Date collapsed into one item.
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("'@2013' in (@2013 | @2014)")]
    [InlineData("@2013 in ('@2013' | 'x')")]
    public void GivenAStringAndATemporalSpellingTheSameText_WhenTestingMembership_ThenItIsNotAMember(
        string expression)
    {
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(false);
    }

    [Fact]
    public void GivenTwoStringLiteralsSpellingTemporals_WhenOrdered_ThenTheyCompareLexically()
    {
        // Same type, so no error - but ordinal, not temporal. Before the fix this answered empty,
        // because both sniffed as temporals whose precisions merely overlap.
        var result = Evaluate("'@2013' < '@2013-01'").Single();

        result.Value.ShouldBe(true);
    }

    [Theory]
    [InlineData("@2013 = @2013", true)]
    [InlineData("@2013 = @2014", false)]
    [InlineData("@2013 < @2013-02", null)]
    [InlineData("@2013-01 < @2013-02", true)]
    [InlineData("@2012-01-01T10:00:00Z = @2012-01-01T20:00:00+10:00", true)]
    [InlineData("$this = @1974-12-25", true)]
    [InlineData("$this < @1975-01-01", true)]
    [InlineData("$this = @2013", false)]
    public void GivenGenuineTemporalOperands_WhenCompared_ThenTheAnswerIsUnchanged(
        string expression, bool? expected)
    {
        // The controls. A genuine temporal literal, and a resource-backed date element reached through
        // $this, must both still route as temporal - including the partial-precision case that answers
        // empty rather than false.
        var results = Evaluate(expression).ToList();

        if (expected is null)
        {
            results.ShouldBeEmpty();
            return;
        }

        results.Single().Value.ShouldBe(expected.Value);
    }

    [Theory]
    [InlineData("@2013.lowBoundary()", "2013-01-01T00:00:00.000+14:00")]
    [InlineData("@2013.highBoundary()", "2013-12-31T23:59:59.999-12:00")]
    [InlineData("@T12:00.lowBoundary()", "12:00:00.000")]
    [InlineData("$this.lowBoundary()", "1974-12-25T00:00:00.000+14:00")]
    public void GivenAGenuineTemporal_WhenTakingABoundary_ThenTheAnswerIsUnchanged(
        string expression, string expected)
    {
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("'@2013'.length()", 5)]
    [InlineData("'@2013'.substring(0,1)", "@")]
    [InlineData("'@2013'.toString()", "@2013")]
    [InlineData("@2013.toString()", "2013")]
    [InlineData("'@2013'.type().name", "String")]
    [InlineData("@2013.type().name", "Date")]
    public void GivenTheShippedSigilInvariants_WhenEvaluated_ThenTheyAreUnchanged(
        string expression, object expected)
    {
        // The invariants PR #427 shipped: the sigil is part of a String's value and no part of a
        // temporal's. This change reads those types rather than the sigil, so it must not disturb them.
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(expected);
    }

    private IEnumerable<IElement> Evaluate(string expression)
    {
        var root = new PrimitiveDateElement("birthDate", "1974-12-25");
        return _evaluator.Evaluate(root, _parser.Parse(expression));
    }

    private sealed class PrimitiveDateElement : IElement
    {
        public PrimitiveDateElement(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }
        public string InstanceType => "date";
        public object? Value { get; }
        public string Location => Name;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }
}
