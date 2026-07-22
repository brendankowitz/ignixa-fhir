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
    public void GivenEqComparator_WhenLowered_ThenBuildsCompoundAndOfStartAndEndConditions()
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
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("StartDateTime");
        le.Value.Value.ShouldBe(value.End);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(value.Start);
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
        // Arrange: "2023-06" resolves to [Jun 1 00:00:00, Jun 30 23:59:59.9999999] -- reference is exactly
        // 1 tick after that interval's End, so tolerance = (End - Start ~ 30 days) / 2 distance / 10 = 36h.
        var parameter = Parameter();
        var value = DateTimeSearchValue.Parse("2023-06");
        var referenceTime = new DateTimeOffset(2023, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);
        var widenedStart = new DateTimeOffset(2023, 5, 30, 12, 0, 0, TimeSpan.Zero);
        var widenedEnd = new DateTimeOffset(2023, 7, 2, 11, 59, 59, TimeSpan.Zero).AddTicks(9999999);

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
    public void GivenApComparator_WhenLoweredWithNoReferenceTime_ThenThrowsInvalidOperationExceptionNamingLowerRun()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);

        // Act & Assert
        var exception = Should.Throw<InvalidOperationException>(() =>
            DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103));
        exception.Message.ShouldContain("Lower.Run");
    }

    [Fact]
    public void GivenApComparator_WhenWidenedStartWouldUnderflowDateTimeOffsetMinValue_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange: value sits 1000 ticks after DateTimeOffset.MinValue; the reference instant is far enough
        // away (20000 ticks) that the resulting 2000-tick tolerance would push the widened Start below
        // DateTimeOffset.MinValue.
        var parameter = Parameter();
        var nearMinInstant = new DateTimeOffset(1000, TimeSpan.Zero);
        var value = new DateTimeSearchValue(nearMinInstant);
        var referenceTime = nearMinInstant + TimeSpan.FromTicks(20000);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() =>
            DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203, referenceTime), 103));
    }

    [Fact]
    public void GivenApComparator_WhenWidenedEndWouldOverflowDateTimeOffsetMaxValue_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange: value sits 1000 ticks before DateTimeOffset.MaxValue; the reference instant is far enough
        // away (20000 ticks) that the resulting 2000-tick tolerance would push the widened End above
        // DateTimeOffset.MaxValue.
        var parameter = Parameter();
        var nearMaxInstant = new DateTimeOffset(DateTimeOffset.MaxValue.UtcTicks - 1000, TimeSpan.Zero);
        var value = new DateTimeSearchValue(nearMaxInstant);
        var referenceTime = nearMaxInstant - TimeSpan.FromTicks(20000);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() =>
            DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203, referenceTime), 103));
    }
}
