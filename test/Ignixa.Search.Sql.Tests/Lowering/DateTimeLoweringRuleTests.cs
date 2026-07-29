using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class DateTimeLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId, DateTimeOffset? approximationReferenceTime = null)
        => new(
            new SymbolTable(
                new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
                new Dictionary<string, short>()),
            approximationReferenceTime);

    private static SearchParameterInfo Parameter()
        => new("date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));

    private static DateTimeSearchValue RangeValue()
        => new(
            new PartialDateTime(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new PartialDateTime(new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.Zero)));

    private static DateTimeSearchValue InstantValue()
        => new(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void GivenEqComparator_WhenLowered_ThenContainsResourceRangeWithinParameterRange()
    {
        // Arrange
        var parameter = Parameter();
        var value = RangeValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)203);
        cte.ResourceTypeId.ShouldBe((short)103);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("StartDateTime");
        ge.Value.Value.ShouldBe(value.Start);
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("EndDateTime");
        le.Value.Value.ShouldBe(value.End);
    }

    [Fact]
    public void GivenNeComparator_WhenLowered_ThenBuildsOrOfStartAndEndConditions()
    {
        // Arrange
        var parameter = Parameter();
        var value = RangeValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ne, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        var lt = or.Left.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("StartDateTime");
        lt.Value.Value.ShouldBe(value.Start);
        var gt = or.Right.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("EndDateTime");
        gt.Value.Value.ShouldBe(value.End);
    }

    [Fact]
    public void GivenLtComparator_WhenLowered_ThenComparesStartDateTimeAgainstSearchStart()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Lt, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("StartDateTime");
        lt.Value.Value.ShouldBe(value.Start);
    }

    [Fact]
    public void GivenGtComparator_WhenLowered_ThenComparesEndDateTimeAgainstSearchEnd()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Gt, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var gt = cte.Predicate.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("EndDateTime");
        gt.Value.Value.ShouldBe(value.End);
    }

    [Fact]
    public void GivenLeComparator_WhenLowered_ThenComparesStartDateTimeAgainstSearchEnd()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Le, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var le = cte.Predicate.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("StartDateTime");
        le.Value.Value.ShouldBe(value.End);
    }

    [Fact]
    public void GivenGeComparator_WhenLowered_ThenComparesEndDateTimeAgainstSearchStart()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var ge = cte.Predicate.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(value.Start);
    }

    [Fact]
    public void GivenSaComparator_WhenLowered_ThenComparesStartDateTimeAgainstSearchEnd()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Sa, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var gt = cte.Predicate.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("StartDateTime");
        gt.Value.Value.ShouldBe(value.End);
    }

    [Fact]
    public void GivenEbComparator_WhenLowered_ThenComparesEndDateTimeAgainstSearchStart()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eb, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("EndDateTime");
        lt.Value.Value.ShouldBe(value.Start);
    }

    // :ap — date approximation: midpoint = Start + (End - Start) / 2;
    // toleranceTicks = abs(referenceTime.UtcTicks - midpoint.UtcTicks) / 10;
    // widened = [Start - tolerance, End + tolerance], compared with the same overlap shape as Eq.
    [Fact]
    public void GivenApComparator_WhenLoweredWithPastInstant_ThenBuildsOverlapAgainstWidenedRange()
    {
        // Arrange: value is exactly one day before the reference instant -- 1-day gap / 10 = 2h24m tolerance.
        var parameter = Parameter();
        var value = new DateTimeSearchValue(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var referenceTime = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);
        var widenedStart = new DateTimeOffset(2019, 12, 31, 21, 36, 0, TimeSpan.Zero);
        var widenedEnd = new DateTimeOffset(2020, 1, 1, 2, 24, 0, TimeSpan.Zero);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203, referenceTime), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)203);
        cte.ResourceTypeId.ShouldBe((short)103);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("StartDateTime");
        le.Value.Value.ShouldBe(widenedEnd);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(widenedStart);
    }

    [Fact]
    public void GivenApComparator_WhenLoweredWithFutureInstant_ThenBuildsOverlapAgainstWidenedRange()
    {
        // Arrange: value is exactly 6 hours after the reference instant -- 6h gap / 10 = 36m tolerance.
        var parameter = Parameter();
        var value = new DateTimeSearchValue(new DateTimeOffset(2030, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var referenceTime = new DateTimeOffset(2030, 6, 15, 6, 0, 0, TimeSpan.Zero);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);
        var widenedStart = new DateTimeOffset(2030, 6, 15, 11, 24, 0, TimeSpan.Zero);
        var widenedEnd = new DateTimeOffset(2030, 6, 15, 12, 36, 0, TimeSpan.Zero);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203, referenceTime), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("StartDateTime");
        le.Value.Value.ShouldBe(widenedEnd);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(widenedStart);
    }

    [Fact]
    public void GivenApComparator_WhenLoweredWithPartialPrecisionValue_ThenWidensThePreservedIntervalBeforeComparing()
    {
        // Arrange: "2023-06" resolves to [Jun 1 00:00:00, Jun 30 23:59:59.9999999]. The proportional term
        // is (midpoint -> reference) / 10 = 36h, but the value's own precision -- one month less one tick
        // -- is larger, so max() selects the precision floor and the interval widens by a full month
        // either side.
        var parameter = Parameter();
        var value = DateTimeSearchValue.Parse("2023-06");
        var referenceTime = new DateTimeOffset(2023, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);
        var widenedStart = new DateTimeOffset(2023, 5, 2, 0, 0, 0, TimeSpan.Zero).AddTicks(1);
        var widenedEnd = new DateTimeOffset(2023, 7, 30, 23, 59, 59, TimeSpan.Zero).AddTicks(9999998);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203, referenceTime), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("StartDateTime");
        le.Value.Value.ShouldBe(widenedEnd);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(widenedStart);
    }

    [Fact]
    public void GivenApComparator_WhenLoweredWithNoReferenceTime_ThenThrowsInvalidOperationExceptionNamingSearchSqlCompiler()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() =>
            DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103));
        exception.Message.ShouldContain("SearchSqlCompiler");
    }

    [Fact]
    public void GivenApComparator_WhenWidenedStartWouldUnderflowDateTimeOffsetMinValue_ThenSaturatesAtMinValue()
    {
        // Arrange: value sits 1000 ticks after DateTimeOffset.MinValue; the reference instant is far enough
        // away (20000 ticks) that the resulting tolerance would push the widened Start below
        // DateTimeOffset.MinValue. date=ap0001-01-01 is legal user input, so this must compile rather than
        // throw past SearchSqlCompiler's trace boundary (which only catches NotSupported/KeyNotFound).
        var parameter = Parameter();
        var nearMinInstant = new DateTimeOffset(1000, TimeSpan.Zero);
        var value = new DateTimeSearchValue(nearMinInstant);
        var referenceTime = nearMinInstant + TimeSpan.FromTicks(20000);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);

        // Act
        var lowered = DateTimeLoweringRule.Lower(
            predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203, referenceTime), 103);

        // Assert — tolerance is 20000/10 = 2000 ticks, so only the lower endpoint saturates. Asserting the
        // endpoints, not merely that something was produced: clamping to MaxValue instead of MinValue would
        // also be non-null, and would invert the range.
        var and = lowered.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("StartDateTime");
        le.Value.Value.ShouldBe(new DateTimeOffset(1000 + 2000, TimeSpan.Zero));
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public void GivenApComparator_WhenWidenedEndWouldOverflowDateTimeOffsetMaxValue_ThenSaturatesAtMaxValue()
    {
        // Arrange: value sits 1000 ticks before DateTimeOffset.MaxValue; the reference instant is far enough
        // away (20000 ticks) that the resulting tolerance would push the widened End above
        // DateTimeOffset.MaxValue. Saturating matches how numeric :ap handles the decimal bounds.
        var parameter = Parameter();
        var nearMaxInstant = new DateTimeOffset(DateTimeOffset.MaxValue.UtcTicks - 1000, TimeSpan.Zero);
        var value = new DateTimeSearchValue(nearMaxInstant);
        var referenceTime = nearMaxInstant - TimeSpan.FromTicks(20000);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);

        // Act
        var lowered = DateTimeLoweringRule.Lower(
            predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203, referenceTime), 103);

        // Assert — mirror image of the underflow case: only the upper endpoint saturates
        var and = lowered.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("StartDateTime");
        le.Value.Value.ShouldBe(DateTimeOffset.MaxValue);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(new DateTimeOffset(DateTimeOffset.MaxValue.UtcTicks - 1000 - 2000, TimeSpan.Zero));
    }

    [Fact]
    public void GivenApComparator_WhenReferenceInstantSitsAtTheValuesMidpoint_ThenToleranceFallsBackToTheValuesOwnPrecision()
    {
        // Arrange: date=ap<today>. The midpoint-to-reference distance is zero, so the proportional 10%
        // term contributes nothing. Without the precision floor the widened range would collapse to exact
        // equality -- the single most likely real-world :ap query silently degrading to :eq. "2026-07-22"
        // spans [00:00:00.0000000, 23:59:59.9999999], so the floor is one day less one tick.
        var parameter = Parameter();
        var value = DateTimeSearchValue.Parse("2026-07-22");
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);
        var midpoint = new DateTimeOffset(
            value.Start.UtcTicks + ((value.End.UtcTicks - value.Start.UtcTicks) / 2), TimeSpan.Zero);
        var widenedStart = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero).AddTicks(1);
        var widenedEnd = new DateTimeOffset(2026, 7, 23, 23, 59, 59, TimeSpan.Zero).AddTicks(9999998);

        // Act
        var cte = DateTimeLoweringRule.Lower(
            predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203, midpoint), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>().Value.Value.ShouldBe(widenedEnd);
        and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Value.Value.ShouldBe(widenedStart);
    }
}
