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
        // The real-world shape of the same defect: before the fix this answered a definite true,
        // because the String operand sniffed as a date and the two were compared as instants - and
        // this fixture's birthDate is 1974-12-25, which does precede 1980-01-01. The old engine
        // therefore over-matched rather than under-matched, which is the opposite remediation: it
        // admitted records a type-correct engine refuses to compare at all, and did not exclude valid
        // ones. GivenDateLookingStringsForTheSameInstant_* in FhirTemporalComparisonTests was flipped
        // for the same reason - the old shape-sniff produced a definite answer, on instant ordering.
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
    [InlineData("@2013.lowBoundary()", "2013-01-01T00:00:00.000+14:00", "dateTime")]
    [InlineData("@2013.highBoundary()", "2013-12-31T23:59:59.999-12:00", "dateTime")]
    [InlineData("@T12:00.lowBoundary()", "12:00:00.000", "time")]
    [InlineData("$this.lowBoundary()", "1974-12-25T00:00:00.000+14:00", "dateTime")]
    public void GivenAGenuineTemporal_WhenTakingABoundary_ThenTheAnswerIsUnchanged(
        string expression, string expected, string expectedType)
    {
        // The instance type is asserted as well as the value, and the two rows that expect dateTime
        // from a Date input pin something known to be wrong: FHIRPath 3.0 lowBoundary says the function
        // "returns the same type as the value in the input collection", so @2013.lowBoundary() should
        // be a Date. That defect predates the type-routing change and is deliberately untouched by it.
        // Asserting the type proves this change is not its cause, and makes fixing it later a
        // deliberate edit here rather than a silent drift.
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(expected);
        result.InstanceType.ShouldBe(expectedType);
    }

    [Theory]
    [InlineData("'@2013'.year()")]
    [InlineData("'2013'.year()")]
    [InlineData("'2013-06-15'.month()")]
    [InlineData("'2013-06-15'.day()")]
    [InlineData("'@2013-06-15T10:30:00'.hour()")]
    [InlineData("'2013-06-15T10:30:00'.minute()")]
    [InlineData("'2013-06-15T10:30:45'.second()")]
    [InlineData("'2013-06-15T10:30:45.123'.millisecond()")]
    [InlineData("'@T12:30'.hour()")]
    [InlineData("'2013-06-15T10:30:00Z'.timezone()")]
    [InlineData("'2013-06-15'.difference(@2014-06-15, 'years')")]
    [InlineData("'2013-06-15'.duration(@2014-06-15)")]
    public void GivenAStringSpellingATemporal_WhenExtractingADateTimeComponent_ThenItIsEmpty(
        string expression)
    {
        // The fifth sniff site. DateTimeFunctions.ParseDateTimeValue stripped a leading sigil and then
        // parsed by shape, consulting InstanceType only for the time special case, so a String reported
        // calendar components it does not have: '@2013'.year() answered 2013 and '2013-06-15'.month()
        // answered 6. All twelve call sites funnel through that one method - ten functions, with
        // difference() and duration() calling it once per operand - so all twelve were affected and one
        // gate fixes all of them.
        //
        // These are Ignixa extensions rather than FHIRPath functions - Firely throws ArgumentException
        // on month() - so no reference engine governs them and nothing external could have caught this.
        //
        // Empty rather than a throw is deliberate, and it is the one place this change leaves the same
        // type mismatch answering two ways: an error under < and an empty under .month(). The reason is
        // recorded in full at ParseDateTimeValue - §Comparison mandates the ordering error and nothing
        // mandates one here, while throwing would cost a whole search parameter at indexing time. These
        // rows are the pin on that decision, so changing them is the deliberate edit that reopens it.
        Evaluate(expression).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("@2013.year()", 2013)]
    [InlineData("@2013-06-15.month()", 6)]
    [InlineData("@2013-06-15.day()", 15)]
    [InlineData("@2013-06-15T10:30:00.hour()", 10)]
    [InlineData("@2013-06-15T10:30:00.minute()", 30)]
    [InlineData("@T12:30:45.second()", 45)]
    [InlineData("$this.year()", 1974)]
    [InlineData("$this.month()", 12)]
    [InlineData("$this.day()", 25)]
    public void GivenAGenuineTemporal_WhenExtractingADateTimeComponent_ThenTheAnswerIsUnchanged(
        string expression, int expected)
    {
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("$this.hour()", 12)]
    [InlineData("$this.minute()", 30)]
    [InlineData("$this.second()", 45)]
    public void GivenAResourceBackedTime_WhenExtractingAComponent_ThenTheAnswerIsUnchanged(
        string expression, int expected)
    {
        // A FhirTemporal-valued element rather than a literal. The gate must admit it by its value as
        // well as by its declared type, and the wire form it yields still carries the time marker that
        // the literal path has already had removed.
        var result = EvaluateOn(TemporalElement("t", "12:30:45", FhirPrimitive.Time, "time"), expression)
            .Single();

        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("@2013 < @T12:00")]
    [InlineData("@T12:00 < @2013")]
    [InlineData("@2013 > @T12:00")]
    [InlineData("@T12:00 >= @2013")]
    [InlineData("@2013-06-15T10:00:00 < @T12:00")]
    [InlineData("@T12:00 <= @2013-06-15T10:00:00")]
    public void GivenATimeOfDayAndACalendarValue_WhenOrdered_ThenItIsATypeError(string expression)
    {
        // Both operands are temporals, so a gate that only asks "is this a temporal?" is blind between
        // them - the first version of this guard was, and these answered empty. The conversion table
        // gives Date/DateTime to Time no entry in either direction, so the Comparison error mandate
        // applies just as it does to String against Date.
        //
        // Firely throws on both orderings, and that is measured rather than asserted: birthDate <
        // @T10:30:00 and birthDate < '1980-01-01' are rows in FirelyParityFixture.ConstructCorpus, and
        // adding them moved KnownDivergences.ConstructPopulation.ExpectedBothThrew from 17 to 19 - both
        // engines throwing on both. The HAPI half is still only read from source: opLessThan matches
        // neither its date/dateTime/instant arm nor its time arm and falls through to
        // FHIRPATH_CANT_COMPARE. No HAPI harness exists here, so that one remains a citation.
        var thrown = Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression).ToList());

        thrown.Message.ShouldContain("must be of the same type");
    }

    [Theory]
    [InlineData("(@2013 | @T12:00).max()")]
    [InlineData("(@T12:00 | @2013).max()")]
    [InlineData("(@2013 | @T12:00).min()")]
    [InlineData("(@T12:00 | @2013).min()")]
    [InlineData("(@2013 | @T12:00).sort()")]
    [InlineData("(@T12:00 | @2013).sort()")]
    public void GivenATimeOfDayAndACalendarValue_WhenAggregatedOrSorted_ThenItIsTheSameTypeError(
        string expression)
    {
        // min()/max()/sort() defer their ordering to the Comparison operators rather than restating it,
        // so they have to reject the operand pair the operators reject. The gate the row above pins was
        // added to the operators and to equality but not to ValueOrdering.CompareTemporals, the third
        // consumer, so these went the other way: FhirTemporal.Compare answered null, Extreme read null as
        // "the incumbent stands", and max() returned whichever operand was written first - the same
        // collection gave two different answers depending on the order of the union. sort() was worse
        // still, placing a time of day among calendar values with no signal at all.
        //
        // The error text differs from the operators' "must be of the same type" because it comes from
        // ValueOrdering.NotOrderable, which names the calling function; that these are the same defect is
        // the point, not that they are the same string.
        var thrown = Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression).ToList());

        thrown.Message.ShouldContain("cannot order operands");
    }

    [Theory]
    [InlineData("(@2013 | @2014).max()", "2014")]
    [InlineData("(@T12:00 | @T13:00).max()", "13:00")]
    [InlineData("(@2013 | @2013-06).sort().count()", 2)]
    public void GivenTemporalsOfOneKind_WhenAggregatedOrSorted_ThenTheAnswerIsUnchanged(
        string expression, object expected)
    {
        // The control. Only the cross-kind pair became an error; same-kind ordering keeps its answer,
        // including the partial-precision pair whose comparison is indeterminate but whose sort is still
        // a total order over both elements.
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(expected);
    }

    [Fact]
    public void GivenAResourceBackedTime_WhenOrderedAgainstADateLiteral_ThenItIsATypeError()
    {
        var thrown = Should.Throw<FhirPathEvaluationException>(
            () => EvaluateOn(TemporalElement("t", "12:30:45", FhirPrimitive.Time, "time"), "$this < @2013")
                .ToList());

        thrown.Message.ShouldContain("must be of the same type");
    }

    [Theory]
    [InlineData("@2013-01-01T10:00:00 < @2013-01-01")]
    [InlineData("@2013-01-01 < @2013-01-01T10:00:00")]
    [InlineData("@2013 < @2013-01")]
    public void GivenADateAndADateTime_WhenOrderedAtOverlappingPrecision_ThenItStaysEmpty(
        string expression)
    {
        // The boundary the Date-versus-Time error must not cross. Date to DateTime is Implicit in the
        // conversion table, so these are one type compared at precisions that merely overlap: a genuine
        // indeterminate empty, not a type error. An error here would be the over-reach.
        Evaluate(expression).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("@T12:00 < @T13:00", true)]
    [InlineData("@T12:00 = @T12:00", true)]
    [InlineData("@2013 = @T12:00", false)]
    [InlineData("@T12:00 = @2013", false)]
    public void GivenTemporalKinds_WhenComparedForEquality_ThenTheAnswerIsUnchanged(
        string expression, bool expected)
    {
        // Equality already drew the Time-versus-calendar line correctly and answers a decidable false
        // rather than an error, per official testDateNotEqualTime*. The ordering fix must not migrate
        // that into a throw - the two operators legitimately differ here, which is why the shared
        // discriminator lives in TemporalOperand.AreComparableKinds and only ordering treats it as an
        // error.
        var result = Evaluate(expression).Single();

        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("'2013' = @2013", true)]
    [InlineData("'2013' ~ @2013", true)]
    [InlineData("'2013' != @2013", false)]
    [InlineData("'2013' !~ @2013", false)]
    public void GivenAStringEqualToATemporalsWireText_WhenComparedForEquality_ThenTheyAreEqual(
        string expression, bool expected)
    {
        // This pins a deliberate non-change, and it is the only thing that does. Equality gained no
        // type guard: a String and a Date whose texts match compare equal, because that is what HAPI
        // answers - doEquals reaches Base.equals(left.primitiveValue(), right.primitiveValue()) with no
        // type-difference check, so "2013" equals "2013".
        //
        // The engines split here. Firely answers false for the same expression. The spec does not
        // settle it: the Equals section states operands "must be of the same type (or be implicitly
        // convertible to the same type)" and then says nothing about what happens when they are not.
        // HAPI was followed because HAPI is Tier 2 of the precedence used throughout this change, and
        // Firely is Tier 3.
        //
        // Every other String-versus-temporal row in this file uses '@2013', whose sigil makes it
        // unequal by text whether or not a blanket type guard exists. Those rows would all stay green
        // if someone added one, so these four are the only rows here that react to the restraint at all.
        //
        // Only the = and != rows guard it. Measured by injecting a blanket "different instance types are
        // never equal" guard at each site in turn: at FunctionHelpers.AreElementsEqual, = and != go red
        // and ~ and !~ stay green; at AreEquivalent, nothing goes red. Equivalence has two independent
        // routes to true - FhirPathEvaluator.AreElementsEquivalent tries AreElementsEqual as a fast path
        // and falls through to AreEquivalent's untyped wire-string comparison when it answers false - and
        // either alone is sufficient, so no single-site guard can move them. The ~ rows corroborate that
        // the restraint holds end to end; they do not detect its removal.
        //
        // The two operators are deliberately not routed through one gate to fix that. Equivalence is a
        // different relation, not a laxer spelling of equality - it rounds decimals, truncates temporals
        // to the lesser precision and ignores string case and whitespace - and the fast-path-then-descend
        // shape of AreElementsEquivalent is what issue #411 needed. Collapsing them so one mutation moves
        // both would trade real semantics for a tidier mutation score.
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
        return EvaluateOn(new PrimitiveDateElement("birthDate", "1974-12-25"), expression);
    }

    private IEnumerable<IElement> EvaluateOn(IElement root, string expression)
    {
        return _evaluator.Evaluate(root, _parser.Parse(expression));
    }

    private static IElement TemporalElement(string name, string literal, FhirPrimitive kind, string instanceType)
    {
        FhirTemporal.TryParse(literal, kind, out var temporal).ShouldBeTrue();
        return new TypedValueElement(name, temporal, instanceType);
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

    private sealed class TypedValueElement : IElement
    {
        public TypedValueElement(string name, object? value, string instanceType)
        {
            Name = name;
            Value = value;
            InstanceType = instanceType;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => Name;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }
}
