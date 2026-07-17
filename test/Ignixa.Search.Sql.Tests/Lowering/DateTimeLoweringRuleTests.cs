using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class DateTimeLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

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
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("EndDateTime");
        lt.Value.Value.ShouldBe(value.Start);
    }

    [Fact]
    public void GivenApComparator_WhenLowered_ThenThrows()
    {
        // Arrange
        var parameter = Parameter();
        var value = InstantValue();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203), 103));
    }
}
