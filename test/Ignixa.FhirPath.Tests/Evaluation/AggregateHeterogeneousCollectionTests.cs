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
/// collection as whatever the head happened to be, comparing quantity units with <c>==</c> and re-parsing
/// temporals to <see cref="DateTime"/> against a fixed list of formats. Every pre-existing test used a
/// single unit throughout - <c>'mg'</c> in one, <c>'Cel'</c> in another - so the string comparison always
/// succeeded and the conversion path was never entered. The official HL7 suites are no help either: they
/// contain no <c>min()</c>, <c>max()</c>, <c>sum()</c> or <c>avg()</c> cases at all in r4, r4b or r5.
/// </para>
/// <para>
/// So the gap being closed here is specifically heterogeneity, and every case is written so the old
/// behaviour gives a different answer - except the two labelled as guards, which pin behaviour the
/// rewrite had to preserve rather than behaviour it changed.
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
    /// sum() has to construct a value, so unlike min()/max() it must name a unit. It uses the first
    /// operand's, which is the only unit in the collection that is not an arbitrary choice.
    /// </summary>
    [Fact]
    public void GivenQuantitiesInCompatibleUnits_WhenSum_ThenTotalsInTheFirstOperandsUnit()
    {
        // Arrange
        var expression = "(1 'm').combine(50 'cm').sum()";

        // Act
        var result = SingleQuantity(expression);

        // Assert
        result.Value.ShouldBe(1.5m);
        result.Unit.ShouldBe("m");
    }

    [Fact]
    public void GivenQuantitiesInCompatibleUnits_WhenAvg_ThenAveragesInTheFirstOperandsUnit()
    {
        // Arrange
        var expression = "(1 'm').combine(50 'cm').avg()";

        // Act
        var result = SingleQuantity(expression);

        // Assert
        result.Value.ShouldBe(0.75m);
        result.Unit.ShouldBe("m");
    }

    /// <summary>
    /// Units that do not relate are FHIRPath's own empty result - "attempting to operate on quantities
    /// with invalid units will result in empty" - not an error and not a guess. All four rows passed
    /// before the fix as well, because comparing unit strings with <c>==</c> gives the right answer when
    /// the units genuinely are unrelated; it is only the compatible-unit cases above that it got wrong.
    /// These rows are here to pin that the conversion did not turn empty into a guess.
    /// </summary>
    [Theory]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("sum")]
    [InlineData("avg")]
    public void GivenQuantitiesInIncompatibleUnits_WhenAggregating_ThenReturnsEmpty(string function)
    {
        // Arrange
        var expression = $"(1 'm').combine(5 'kg').{function}()";

        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// The headline case for the head-decides-the-branch defect. A leading integer routed the whole
    /// collection down the numeric path, which skipped every element it could not read as a number, so
    /// min() answered <c>1</c> while quietly discarding the quantity it was supposed to be compared
    /// against. FHIRPath's implicit Integer-to-Quantity conversion gives <c>1</c> the unity unit, which
    /// does not relate to <c>'mg'</c>, so the honest answer is empty.
    /// </summary>
    [Fact]
    public void GivenANumberBeforeAQuantity_WhenMin_ThenTheNumberDoesNotDecideTheBranch()
    {
        // Arrange
        var expression = "(1).combine(5 'mg').min()";

        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// The same collection with the operands the other way round, so that neither arrangement is the one
    /// the implementation happens to handle. This one passed before the fix as well, but for a reason
    /// that does not generalise: a leading quantity sent the collection down the quantity branch, which
    /// bailed to empty on meeting anything that was not a quantity. The right answer, reached by
    /// discarding the operand rather than by relating it.
    /// </summary>
    [Fact]
    public void GivenAQuantityBeforeANumber_WhenMax_ThenTheQuantityDoesNotDecideTheBranch()
    {
        // Arrange
        var expression = "(5 'mg').combine(1).max()";

        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// A leading string routed the collection down the lexicographic path, which skipped the number and
    /// answered with the only element it could read. A string and a number have no ordering between them,
    /// so this is an error - the same answer <c>sort()</c> gives, and the same answer FHIRPath's own
    /// comparison operators give.
    /// </summary>
    [Fact]
    public void GivenAStringBeforeANumber_WhenMin_ThenTheStringDoesNotDecideTheBranch()
    {
        // Arrange
        var expression = "('apple').combine(3).min()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
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
    public void GivenMixedTypes_WhenSum_ThenThrows()
    {
        // Arrange
        var expression = "(3).combine('apple').sum()";

        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
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
    /// One instant written at two offsets is one value. The old comparison never normalised - date
    /// literals fell through to an ordinal compare of the wire text - so <c>10:00:00Z</c> sorted before
    /// <c>20:00:00+10:00</c> on the strength of the eleventh character.
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
    /// The companion assertion: if the two really are equal then max() must select the same element that
    /// min() did. This half passed before the fix too - ordinal text happens to rank <c>"20:00:00+10:00"</c>
    /// above <c>"10:00:00Z"</c>, which is the same element - so the min() case above carries the proof and
    /// this one holds the pair together.
    /// </summary>
    [Fact]
    public void GivenTheSameInstantInTwoOffsets_WhenMax_ThenSelectsTheSameElementAsMin()
    {
        // Arrange
        var expression = "@2012-01-15T20:00:00+10:00.combine(@2012-01-15T10:00:00Z).max()";

        // Act
        var result = Render(expression);

        // Assert
        result.ShouldBe(["2012-01-15T20:00:00+10:00"]);
    }

    /// <summary>
    /// A year and a day inside it overlap as intervals, so neither precedes the other and there is no
    /// extreme to report. The old code parsed against a fixed format list that has no <c>yyyy</c> entry,
    /// so <c>@2012</c> failed to parse and was dropped from the collection entirely - and then, because
    /// the head was a string, the ordinal path answered <c>"2012"</c> anyway.
    /// </summary>
    [Fact]
    public void GivenTemporalsAtDifferentPrecisions_WhenMin_ThenTheExtremeIsIndeterminate()
    {
        // Arrange
        var expression = "@2012.combine(@2012-06-15).min()";

        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// A local time carries no offset, so it could sit at any of them and overlaps a fixed instant rather
    /// than ordering against it. The old parse applied <c>AssumeUniversal</c>, inventing the offset the
    /// value does not have.
    /// </summary>
    [Fact]
    public void GivenAFloatingLocalTimeAndAFixedInstant_WhenMin_ThenTheExtremeIsIndeterminate()
    {
        // Arrange
        var expression = "@2024-01-10T10:00:00Z.combine(@2024-01-10T05:00:00).min()";

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
    /// Guard, not a fix: totalling now routes through a unit conversion, and a unit UCUM has never heard
    /// of has no conversion. It must still total against itself rather than collapsing to empty.
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
    /// sum() answers 0 for an empty collection, and the seed has to stay tied to emptiness rather than to
    /// "nothing was left after filtering". <c>Patient.name</c> is two elements that carry no primitive
    /// value; totalling them to 0 would be a confident answer about a collection of HumanNames.
    /// </summary>
    [Fact]
    public void GivenElementsThatCarryNoValue_WhenSum_ThenReturnsEmptyRatherThanTheEmptyCollectionSeed()
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
