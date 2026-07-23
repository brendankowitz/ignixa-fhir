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

public class TokenNumberNumberLoweringRuleTests
{
    private static LeafContext ContextResolving(
        SearchParameterInfo compositeParameter,
        short searchParamId,
        IReadOnlyDictionary<string, int?>? systemIds = null)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>(),
            compartmentMembership: null,
            systemIds: systemIds));

    private static EmittedSql EmitSql(CteDefinition.ParamSource cte)
        => SqlBuilder.Run(new QueryPlan([cte], new CteRef(0)));

    private static SearchParameterInfo CompositeParameter()
        => new("component-code-value-number-number", "component-code-value-number-number", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-component-code-value-number-number"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenACodeAndTwoUnqualifiedNumberComponents_WhenLowered_ThenComparesCode1AndBothLowHighPairs()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ge, modifier: null, new NumberSearchValue(5m)),
            new(ComponentParameter("high"), SearchComparator.Le, modifier: null, new NumberSearchValue(10m)),
        };

        // Act
        var cte = TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302), 104);

        // Assert
        cte.SearchParamId.ShouldBe((short)302);
        cte.ResourceTypeId.ShouldBe((short)104);
        cte.Table.TableName.ShouldBe("TokenNumberNumberCompositeSearchParam");
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = inner.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        tokenPredicate.Value.Value.ShouldBe("8480-6");
        // ge is High >= S and le is Low <= E, so each slot names the column on the far side of its own
        // range -- the same relation NumberLoweringRuleTests pins at the leaf.
        var number1Predicate = inner.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        number1Predicate.Column.Column.ShouldBe("HighValue2");
        var number2Predicate = outer.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        number2Predicate.Column.Column.ShouldBe("LowValue3");
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenComparesSystemId1AndCode1()
    {
        // Arrange — system|code on the token slot
        var composite = CompositeParameter();
        var systemIds = new Dictionary<string, int?> { ["http://loinc.org"] = 42 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ge, modifier: null, new NumberSearchValue(5m)),
            new(ComponentParameter("high"), SearchComparator.Le, modifier: null, new NumberSearchValue(10m)),
        };

        // Act
        var cte = TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302, systemIds), 104);

        // Assert
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        var tokenAnd = inner.Left.ShouldBeOfType<Predicate.And>();
        var systemEqual = tokenAnd.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId1");
        systemEqual.Value.Value.ShouldBe(42);
        var codeEqual = tokenAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code1");
        codeEqual.Value.Value.ShouldBe("8480-6");
    }

    // :ap composite proof — first numeric slot (LowValue2/HighValue2) independently widened via
    // NumericRangeComparison.Build while the second slot (components[2]) remains a plain Ge; the
    // shared comparator is unchanged, this only proves the first slot dispatches through it in situ.
    // 5.4m: tol = max(pm=0.05, abs(5.4)*0.10=0.54) = 0.54 → overlap against [4.86, 5.94] (same as leaf NumberLoweringRuleTests).
    [Fact]
    public void GivenApComparatorOnFirstNumberSlotAndNonApSecondSlot_WhenLowered_ThenWidensOnlyLowHighValue2()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ap, modifier: null, new NumberSearchValue(5.4m)),
            new(ComponentParameter("high"), SearchComparator.Ge, modifier: null, new NumberSearchValue(10m)),
        };

        // Act
        var cte = TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302), 104);

        // Assert — And(And(tokenPredicate, And(Le(LowValue2,5.94), Ge(HighValue2,4.86))), Ge(HighValue3,10))
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = inner.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var number1Range = inner.Right.ShouldBeOfType<Predicate.And>();
        var number1Le = number1Range.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        number1Le.Column.Column.ShouldBe("LowValue2");
        number1Le.Value.Value.ShouldBe(5.94m);
        var number1Ge = number1Range.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        number1Ge.Column.Column.ShouldBe("HighValue2");
        number1Ge.Value.Value.ShouldBe(4.86m);
        var number2Predicate = outer.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        number2Predicate.Column.Column.ShouldBe("HighValue3");
        number2Predicate.Value.Value.ShouldBe(10m);

        // Assert — complete emitted SQL and ordered parameters: token, widened upper, widened lower,
        // then the non-Ap second numeric slot (@p0 Code1, @p1 LowValue2 upper, @p2 HighValue2 lower, @p3 HighValue3).
        var emitted = EmitSql(cte);
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenNumberNumberCompositeSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 302 AND ((Code1 = @p0 AND (LowValue2 <= @p1 AND HighValue2 >= @p2)) AND HighValue3 >= @p3)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(4);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", "8480-6"));
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", 5.94m));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", 4.86m));
        emitted.Parameters[3].ShouldBe(new EmittedSqlParameter("@p3", 10m));
    }

    // :ap composite proof — second numeric slot (LowValue3/HighValue3) independently widened while the
    // first slot (components[1]) remains a plain Ge, mirroring the previous test with slots swapped so
    // both composite numeric positions are proven to reach NumericRangeComparison.Build independently.
    [Fact]
    public void GivenApComparatorOnSecondNumberSlotAndNonApFirstSlot_WhenLowered_ThenWidensOnlyLowHighValue3()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ge, modifier: null, new NumberSearchValue(10m)),
            new(ComponentParameter("high"), SearchComparator.Ap, modifier: null, new NumberSearchValue(5.4m)),
        };

        // Act
        var cte = TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302), 104);

        // Assert — And(And(tokenPredicate, Ge(HighValue2,10)), And(Le(LowValue3,5.94), Ge(HighValue3,4.86)))
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = inner.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var number1Predicate = inner.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        number1Predicate.Column.Column.ShouldBe("HighValue2");
        number1Predicate.Value.Value.ShouldBe(10m);
        var number2Range = outer.Right.ShouldBeOfType<Predicate.And>();
        var number2Le = number2Range.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        number2Le.Column.Column.ShouldBe("LowValue3");
        number2Le.Value.Value.ShouldBe(5.94m);
        var number2Ge = number2Range.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        number2Ge.Column.Column.ShouldBe("HighValue3");
        number2Ge.Value.Value.ShouldBe(4.86m);

        // Assert — complete emitted SQL and ordered parameters: token, non-Ap first numeric, then the
        // mirrored approximate upper/lower on the second slot (@p0 Code1, @p1 HighValue2, @p2 LowValue3 upper, @p3 HighValue3 lower).
        var emitted = EmitSql(cte);
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenNumberNumberCompositeSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 302 AND ((Code1 = @p0 AND HighValue2 >= @p1) AND (LowValue3 <= @p2 AND HighValue3 >= @p3))\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(4);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", "8480-6"));
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", 10m));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", 5.94m));
        emitted.Parameters[3].ShouldBe(new EmittedSqlParameter("@p3", 4.86m));
    }
}
