using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class TokenDateTimeLoweringRuleTests
{
    private static LeafContext ContextResolving(
        SearchParameterInfo compositeParameter,
        short searchParamId,
        IReadOnlyDictionary<string, int?>? systemIds = null,
        DateTimeOffset? approximationReferenceTime = null)
        => new(
            new SymbolTable(
                new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
                new Dictionary<string, short>(),
                compartmentMembership: null,
                systemIds: systemIds),
            approximationReferenceTime);

    private static EmittedSql EmitSql(CteDefinition.ParamSource cte)
        => SqlBuilder.Run(new QueryPlan([cte], new CteRef(0)));

    private static SearchParameterInfo CompositeParameter()
        => new("code-value-date", "code-value-date", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-code-value-date"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenATokenComponentAndADateTimeComponent_WhenLowered_ThenComparesCode1AndStartEndDateTime2()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var dateParam = ComponentParameter("value-date");
        var dateValue = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(dateParam, SearchComparator.Ge, modifier: null, dateValue),
        };

        // Act
        var cte = TokenDateTimeLoweringRule.Lower(composite, components, ContextResolving(composite, 403), 104);

        // Assert
        cte.SearchParamId.ShouldBe((short)403);
        cte.ResourceTypeId.ShouldBe((short)104);
        cte.Table.TableName.ShouldBe("TokenDateTimeCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        tokenPredicate.Value.Value.ShouldBe("8480-6");
        var datePredicate = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        datePredicate.Column.Column.ShouldBe("EndDateTime2");
        datePredicate.Value.Value.ShouldBe(dateValue.Start);
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenComparesSystemId1AndCode1()
    {
        // Arrange — system|code on the token slot
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var dateParam = ComponentParameter("value-date");
        var systemIds = new Dictionary<string, int?> { ["http://loinc.org"] = 42 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(dateParam, SearchComparator.Ge, modifier: null, new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero))),
        };

        // Act
        var cte = TokenDateTimeLoweringRule.Lower(composite, components, ContextResolving(composite, 403, systemIds), 104);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenAnd = and.Left.ShouldBeOfType<Predicate.And>();
        var systemEqual = tokenAnd.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId1");
        systemEqual.Value.Value.ShouldBe(42);
        var codeEqual = tokenAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code1");
        codeEqual.Value.Value.ShouldBe("8480-6");
    }

    // :ap composite proof — date slot dispatches through DateTimeRangeComparison.Build with an explicit
    // fixed ApproximationReferenceTime, identical widening formula as the leaf DateTimeLoweringRuleTests
    // "past instant" case, while the unqualified token slot (Code1) is retained ahead of it.
    // value is exactly one day before the reference instant -- 1-day gap / 10 = 2h24m tolerance.
    [Fact]
    public void GivenApComparatorOnDateSlotWithFixedReferenceTime_WhenLowered_ThenWidensStartEndDateTime2AndRetainsToken()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var dateParam = ComponentParameter("value-date");
        var dateValue = new DateTimeSearchValue(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var referenceTime = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var widenedStart = new DateTimeOffset(2019, 12, 31, 21, 36, 0, TimeSpan.Zero);
        var widenedEnd = new DateTimeOffset(2020, 1, 1, 2, 24, 0, TimeSpan.Zero);
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(dateParam, SearchComparator.Ap, modifier: null, dateValue),
        };

        // Act
        var cte = TokenDateTimeLoweringRule.Lower(
            composite, components, ContextResolving(composite, 403, approximationReferenceTime: referenceTime), 104);

        // Assert — And(tokenPredicate, And(Le(StartDateTime2,widenedEnd), Ge(EndDateTime2,widenedStart)))
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        tokenPredicate.Value.Value.ShouldBe("8480-6");
        var dateRange = and.Right.ShouldBeOfType<Predicate.And>();
        var startLe = dateRange.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        startLe.Column.Column.ShouldBe("StartDateTime2");
        startLe.Value.Value.ShouldBe(widenedEnd);
        var endGe = dateRange.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        endGe.Column.Column.ShouldBe("EndDateTime2");
        endGe.Value.Value.ShouldBe(widenedStart);

        // Assert — complete emitted SQL and ordered parameters: token before the widened end/start
        // parameters, in the emitter's overlap order (@p0 Code1, @p1 widenedEnd, @p2 widenedStart).
        var emitted = EmitSql(cte);
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenDateTimeCompositeSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 403 AND (Code1 = @p0 AND (StartDateTime2 <= @p1 AND EndDateTime2 >= @p2))\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(3);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", "8480-6"));
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", widenedEnd));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", widenedStart));
    }
}
