/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * min(), max(), sum() and avg() over collections that are not uniform.
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
/// Covers the aggregate functions over collections whose elements do not all look like the first one.
/// </summary>
/// <remarks>
/// <para>
/// Each function used to choose a per-type branch from <c>list[0].Value</c> and then process the whole
/// collection as whatever the head happened to be, comparing quantity units with <c>==</c>. Almost every
/// pre-existing test used a single unit throughout - <c>'mg'</c> in one, <c>'Cel'</c> in another - so the
/// string comparison always succeeded and the conversion path was never entered. The one exception,
/// <c>((5 'mg') | (1 'kg')).sum()</c>, asserted the wrong answer and was corrected with the rewrite. The
/// official HL7 suites are no help either: <c>tests-fhir-r4.xml</c> and <c>tests-fhir-r5.xml</c> in
/// <c>FHIR/fhir-test-cases</c> contain no <c>min()</c>, <c>max()</c>, <c>sum()</c> or <c>avg()</c> case
/// at all - only the general-purpose <c>aggregate()</c>. Checked 2026-08-19.
/// </para>
/// <para>
/// So the gap being closed here is specifically heterogeneity, and cases are written so that the
/// behaviour they were added to change gives a different answer. Which cases those are depends on which
/// baseline is meant, and the file now spans two: the pre-rewrite ladder, and the rewrite's own first
/// pass, which fixed the unit comparison but resolved an indeterminate comparison by abandoning the
/// result. Several cases here were non-vacuous against the first and are not against the second. Rather
/// than assert a count that goes stale, each summary says what its case demonstrates and whether it pins
/// behaviour or changes it.
/// </para>
/// <para>
/// <c>combine()</c> rather than <c>|</c> throughout, because union deduplicates and would silently
/// collapse the two-spellings-of-one-instant cases to a single element before the aggregate ever ran.
/// </para>
/// </remarks>
public class AggregateHeterogeneousCollectionTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R5.GetSchemaProvider();

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "id": "p1",
      "name": [ { "family": "Smith", "given": ["John"] }, { "family": "Jones", "given": ["Ann"] } ]
    }
    """;

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// The extreme is returned in its own unit. min()/max() select an element; they do not construct one,
    /// which is the same rule that keeps a resource-backed temporal from being flattened to a wire string.
    /// </summary>
    [Fact]
    public void GivenQuantitiesInCompatibleUnits_WhenMin_ThenReturnsTheTrueExtremeInItsOwnUnit()
    {
        // Arrange
        var expression = "(1 'm').combine(50 'cm').min()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["50 'cm'"]);
    }

    [Fact]
    public void GivenQuantitiesInCompatibleUnits_WhenMax_ThenReturnsTheTrueExtremeInItsOwnUnit()
    {
        // Arrange
        var expression = "(1 'm').combine(50 'cm').max()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["1 'm'"]);
    }

    [Fact]
    public void GivenMilligramsAndKilograms_WhenMin_ThenReturnsTheSmallerMass()
    {
        // Arrange
        var expression = "(1 'kg').combine(5 'mg').min()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["5 'mg'"]);
    }

    [Fact]
    public void GivenMilligramsAndKilograms_WhenMax_ThenReturnsTheLargerMass()
    {
        // Arrange
        var expression = "(5 'mg').combine(1 'kg').max()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["1 'kg'"]);
    }

    /// <summary>
    /// §Math: "When the units of quantity arguments are different, the quantity values must be converted
    /// to the most granular unit, then simple addition on the values can be performed", with the worked
    /// example <c>3 'm' + 3 'cm' // 303 'cm'</c>. §Unit Conversions defines which unit that is: "selecting
    /// the conversion factor that is less than 1 when converting from one unit to the other". sum() has to
    /// construct a value, so unlike min()/max() it must name a unit, and the spec names which one.
    /// </summary>
    [Fact]
    public void GivenQuantitiesInCompatibleUnits_WhenSum_ThenTotalsInTheMostGranularUnit()
    {
        // Arrange
        var expression = "(1 'm').combine(50 'cm').sum()";

        // Act
        var result = SingleQuantity(expression);

        // Assert
        result.Value.ShouldBe(150m);
        result.Unit.ShouldBe("cm");
    }

    [Fact]
    public void GivenQuantitiesInCompatibleUnits_WhenAvg_ThenAveragesInTheMostGranularUnit()
    {
        // Arrange
        var expression = "(1 'm').combine(50 'cm').avg()";

        // Act
        var result = SingleQuantity(expression);

        // Assert
        result.Value.ShouldBe(75m);
        result.Unit.ShouldBe("cm");
    }

    /// <summary>
    /// The granular unit is chosen by comparing the units, not by position, so the same collection totals
    /// to the same answer whichever operand leads. Its twin - the mg-first spelling, which the
    /// first-operand rule also happened to get right - is
    /// <c>FhirPathAggregateTests.GivenQuantitiesInCompatibleUnits_WhenSum_ThenConvertsAndTotals</c>; only
    /// the pair distinguishes the two rules.
    /// </summary>
    [Fact]
    public void GivenTheCoarserUnitFirst_WhenSum_ThenStillTotalsInTheMostGranularUnit()
    {
        // Arrange
        var expression = "((1 'kg') | (5 'mg')).sum()";

        // Act
        var result = SingleQuantity(expression);

        // Assert
        result.Value.ShouldBe(1000005m);
        result.Unit.ShouldBe("mg");
    }

    /// <summary>
    /// Units that do not relate are FHIRPath's own empty result for a constructed total - "attempting to
    /// operate on quantities with invalid units will result in empty". Both rows passed before the fix as
    /// well, because comparing unit strings with <c>==</c> gives the right answer when the units genuinely
    /// are unrelated; it is only the compatible-unit cases above that it got wrong. They are here to pin
    /// that the conversion did not turn empty into a guess.
    /// </summary>
    [Theory]
    [InlineData("sum")]
    [InlineData("avg")]
    public void GivenQuantitiesInIncompatibleUnits_WhenTotalling_ThenReturnsEmpty(string function)
    {
        // Arrange
        var expression = $"(1 'm').combine(5 'kg').{function}()";

        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// min()/max() select rather than construct, so unrelated units do not stop them: the spec's
    /// equivalence is <c>aggregate(iif($total.empty(), $this, iif($this &lt; $total, $this, $total)))</c>,
    /// whose <c>iif</c> takes the otherwise-branch on an empty criterion and hands back the incumbent. The
    /// fold never yields empty, so neither do these.
    /// </summary>
    [Theory]
    [InlineData("min")]
    [InlineData("max")]
    public void GivenQuantitiesInIncompatibleUnits_WhenSelectingAnExtreme_ThenTheIncumbentStands(string function)
    {
        // Arrange
        var expression = $"(1 'm').combine(5 'kg').{function}()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["1 'm'"]);
    }

    /// <summary>
    /// The headline case for the head-decides-the-branch defect. A leading integer routed the whole
    /// collection down the numeric path, which skipped every element it could not read as a number, so
    /// min() answered <c>1</c> while quietly discarding the quantity it was supposed to be compared
    /// against. FHIRPath's implicit Integer-to-Quantity conversion gives <c>1</c> the unity unit, which
    /// does not relate to <c>'mg'</c>, so the comparison is empty and the incumbent stands - the same
    /// element the old code returned, now for a reason that survives reordering, which the next case is.
    /// </summary>
    [Fact]
    public void GivenANumberBeforeAQuantity_WhenMin_ThenTheIncumbentStands()
    {
        // Arrange
        var expression = "(1).combine(5 'mg').min()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["1"]);
    }

    /// <summary>
    /// The same collection with the operands the other way round, so that neither arrangement is the one
    /// the implementation happens to handle. A leading quantity used to send the collection down the
    /// quantity branch, which bailed to empty on meeting anything that was not a quantity.
    /// </summary>
    [Fact]
    public void GivenAQuantityBeforeANumber_WhenMax_ThenTheIncumbentStands()
    {
        // Arrange
        var expression = "(5 'mg').combine(1).max()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["5 'mg'"]);
    }

    /// <summary>
    /// The unity unit relates to itself, so the Integer-to-Quantity conversion has a positive direction as
    /// well as the empty one above: <c>5</c> really is greater than <c>1 '1'</c>.
    /// </summary>
    [Fact]
    public void GivenAUnityQuantityAndANumber_WhenMax_ThenTheyCompareAsQuantities()
    {
        // Arrange
        var expression = "(1 '1').combine(5).max()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["5"]);
    }

    [Fact]
    public void GivenANumberAndAUnityQuantity_WhenMin_ThenTheyCompareAsQuantities()
    {
        // Arrange
        var expression = "(5).combine(1 '1').min()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["1 '1'"]);
    }

    /// <summary>
    /// A leading string routed the collection down the lexicographic path, which skipped the number and
    /// answered with the only element it could read. A string and a number have no ordering between them,
    /// so this is an error - the same answer <c>sort()</c> gives, and the same answer FHIRPath's own
    /// comparison operators give. The message names the caller, which is the only reason
    /// <c>CompareValues</c> takes a function name at all. It names the candidate before the incumbent,
    /// which is the order the fold compares them in rather than the order they were written.
    /// </summary>
    [Fact]
    public void GivenAStringBeforeANumber_WhenMin_ThenAnErrorNamingTheCallerIsSignalled()
    {
        // Arrange
        var expression = "('apple').combine(3).min()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        var error = Should.Throw<FhirPathEvaluationException>(evaluate);
        error.Message.ShouldBe("min() cannot order operands of type 'integer' and 'string'.");
    }

    [Fact]
    public void GivenANumberBeforeAString_WhenMax_ThenTheNumberDoesNotDecideTheBranch()
    {
        // Arrange
        var expression = "(3).combine('apple').max()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    [Fact]
    public void GivenMixedTypes_WhenSum_ThenAnErrorNamingTheCallerIsSignalled()
    {
        // Arrange
        var expression = "(3).combine('apple').sum()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        var error = Should.Throw<FhirPathEvaluationException>(evaluate);
        error.Message.ShouldBe("sum() cannot total an operand of type 'string'.");
    }

    [Fact]
    public void GivenMixedTypes_WhenAvg_ThenThrows()
    {
        // Arrange
        var expression = "(3).combine('apple').avg()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    /// <summary>
    /// The type gate belongs to the collection, not to the pair. A single unsummable element used to skip
    /// the check entirely and come back unchanged, so <c>('apple').sum()</c> answered <c>'apple'</c> while
    /// <c>('apple' | 'pear').sum()</c> raised - the same question answered two ways depending only on how
    /// many items reached it.
    /// </summary>
    [Theory]
    [InlineData("('apple').sum()")]
    [InlineData("('apple').avg()")]
    [InlineData("(true).sum()")]
    public void GivenASingleUnsummableElement_WhenTotalling_ThenThrows(string expression)
    {
        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    /// <summary>
    /// Boolean is not among the types FHIRPath's Comparison section defines an ordering for, and it is an
    /// <see cref="IComparable"/> that would otherwise order itself without anyone noticing. The
    /// single-element case below is the one that still comes back, because no comparison runs.
    /// </summary>
    [Fact]
    public void GivenTwoBooleans_WhenMin_ThenAnErrorIsSignalled()
    {
        // Arrange
        var expression = "(true).combine(false).min()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    /// <summary>
    /// One instant written at two offsets is one value. The old comparison never normalised - date
    /// literals fell through to an ordinal compare of the wire text - so <c>10:00:00Z</c> sorted before
    /// <c>20:00:00+10:00</c> on the strength of the twelfth character.
    /// </summary>
    [Fact]
    public void GivenTheSameInstantInTwoOffsets_WhenMin_ThenNeitherIsLessAndTheFirstStands()
    {
        // Arrange
        var expression = "@2012-01-15T20:00:00+10:00.combine(@2012-01-15T10:00:00Z).min()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2012-01-15T20:00:00+10:00"]);
    }

    /// <summary>
    /// The companion assertion: if the two really are equal then max() must select the incumbent too. The
    /// operands are the other way round from the min() case above for the same reason the number-and-
    /// quantity pair is written both ways - with the offset spelling leading, ordinal text happens to rank
    /// it highest and the old code reached the same element by the wrong route.
    /// </summary>
    [Fact]
    public void GivenTheSameInstantInTwoOffsets_WhenMax_ThenNeitherIsGreaterAndTheFirstStands()
    {
        // Arrange
        var expression = "@2012-01-15T10:00:00Z.combine(@2012-01-15T20:00:00+10:00).max()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2012-01-15T10:00:00Z"]);
    }

    /// <summary>
    /// A year and a day inside it overlap as intervals, so neither precedes the other and the comparison
    /// is empty. The incumbent stands, per the spec's <c>iif</c>.
    /// </summary>
    [Fact]
    public void GivenTemporalsAtDifferentPrecisions_WhenMin_ThenTheIncumbentStands()
    {
        // Arrange
        var expression = "@2012.combine(@2012-06-15).min()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2012"]);
    }

    /// <summary>
    /// An indeterminate pair must not stop the fold looking. <c>@2011</c> orders determinately against
    /// both of the others and is the minimum of the collection whichever way it is written, but the fold
    /// used to abandon the whole result on meeting the first empty comparison - so the answer depended on
    /// where the incomparable pair happened to fall.
    /// </summary>
    [Theory]
    [InlineData("@2011.combine(@2012).combine(@2012-06-15)")]
    [InlineData("@2012.combine(@2012-06-15).combine(@2011)")]
    [InlineData("@2012-06-15.combine(@2011).combine(@2012)")]
    public void GivenAnIndeterminatePairAndADominatingExtreme_WhenMin_ThenTheExtremeWinsInAnyOrder(string collection)
    {
        // Act
        var result = Render($"{collection}.min()");

        // Assert
        result.ShouldBe(["2011"]);
    }

    /// <summary>
    /// A local time carries no offset, so it could sit at any of them and overlaps a fixed instant rather
    /// than ordering against it. The comparison is empty and the incumbent stands.
    /// </summary>
    [Fact]
    public void GivenAFloatingLocalTimeAndAFixedInstant_WhenMin_ThenTheIncumbentStands()
    {
        // Arrange
        var expression = "@2024-01-10T10:00:00Z.combine(@2024-01-10T05:00:00).min()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2024-01-10T10:00:00Z"]);
    }

    /// <summary>
    /// §Math: "Operations that cause arithmetic overflow or underflow will result in empty ({ })", and
    /// sum() is defined as repeated <c>+</c>. It has to be empty rather than an error, because
    /// <c>FhirPathEvaluator</c> already answers empty for the same overflow reached through the operator
    /// and the two must not disagree - and rather than a saturated total, which would be a wrong answer
    /// presented as a right one.
    /// </summary>
    [Fact]
    public void GivenATotalTooLargeForADecimal_WhenSum_ThenReturnsEmpty()
    {
        // Arrange
        var expression = "(70000000000000000000000000000.0).combine(70000000000000000000000000000.0).sum()";

        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// Guard, not a fix: a single element is trivially its own extreme, so no comparison runs and a value
    /// with no ordering defined for it still comes back rather than raising.
    /// </summary>
    [Fact]
    public void GivenASingleElementOfAnUnorderableType_WhenMin_ThenReturnsThatElement()
    {
        // Arrange
        var expression = "(true).min()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["True"]);
    }

    /// <summary>
    /// Guard, not a fix: totalling routes through a unit conversion, and a unit UCUM has never heard of
    /// has no conversion. It must still total against itself rather than collapsing to empty.
    /// </summary>
    [Fact]
    public void GivenQuantitiesInAUnitUcumDoesNotKnow_WhenSum_ThenTheyStillTotal()
    {
        // Arrange
        var expression = "(5 'widget').combine(3 'widget').sum()";

        // Act
        var result = SingleQuantity(expression);

        // Assert
        result.Value.ShouldBe(8m);
        result.Unit.ShouldBe("widget");
    }

    /// <summary>
    /// <c>Patient.name</c> is two elements that carry no primitive value, and totalling them would be a
    /// confident answer about a collection of HumanNames. The screen they fall through no longer tests
    /// <c>IElement.Value</c> alone - a resource-backed Quantity carries none either - so this pins that
    /// the widening did not admit them.
    /// </summary>
    /// <remarks>
    /// This used to be the case that distinguished the empty collection, which <c>sum()</c> answered with
    /// <c>0</c>, from a collection filtered down to nothing, which it answered with empty. §sum() says
    /// the empty collection is empty too, so there is no longer a distinction to protect and the two
    /// answers coincide. The case is kept because the screen it exercises is still there and still has to
    /// reject a complex element while accepting a resource-backed Quantity.
    /// </remarks>
    [Fact]
    public void GivenElementsThatCarryNoValue_WhenSum_ThenTheyAreNotTotalled()
    {
        // Arrange
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(Schema);
        var parsed = _parser.Parse("Patient.name.sum()");

        // Act
        var result = _evaluator.Evaluate(subject, parsed).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    private FhirQuantity SingleQuantity(string expression) =>
        Evaluate(expression).ShouldHaveSingleItem().Value.ShouldBeOfType<FhirQuantity>();

    private List<string?> Render(string expression) =>
        Evaluate(expression).Select(element => element.Value?.ToString()).ToList();

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
