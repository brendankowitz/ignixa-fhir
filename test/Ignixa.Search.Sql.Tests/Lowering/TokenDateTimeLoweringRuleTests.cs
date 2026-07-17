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

public class TokenDateTimeLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

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
        var cte = TokenDateTimeLoweringRule.Lower(composite, components, ContextResolving(composite, 403));

        // Assert
        cte.SearchParamId.ShouldBe((short)403);
        cte.Table.TableName.ShouldBe("TokenDateTimeCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var datePredicate = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        datePredicate.Column.Column.ShouldBe("EndDateTime2");
        datePredicate.Value.Value.ShouldBe(dateValue.Start);
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var dateParam = ComponentParameter("value-date");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(dateParam, SearchComparator.Ge, modifier: null, new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero))),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenDateTimeLoweringRule.Lower(composite, components, ContextResolving(composite, 403)));
    }
}
