using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class TokenTextLoweringRuleTests
{
    private static SearchParameterInfo CodeParameter()
        => new("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));

    private static SymbolTable Symbols(SearchParameterInfo parameter)
        => new(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Observation"] = 96 });

    [Fact]
    public void GivenATextModifiedToken_WhenLowered_ThenReadsTokenTextRatherThanTokenSearchParam()
    {
        // Arrange -- Observation?code:text=aux. The binder turns a :text modifier into a StringExpression
        // over FieldName.TokenText, which is stored in its own dbo.TokenText table, not in the token
        // table's Code column.
        var parameter = CodeParameter();
        var tree = new SearchParameterExpression(
            parameter,
            new StringExpression(StringOperator.StartsWith, FieldName.TokenText, componentIndex: null, "aux", ignoreCase: true));

        // Act
        var plan = Lower.Run(tree, Symbols(parameter), targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var source = plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ParamSource>();
        source.Table.TableName.ShouldBe("TokenText");
        source.SearchParamId.ShouldBe((short)202);
        source.ResourceTypeId.ShouldBe((short)96);
        source.Predicate.ShouldBeOfType<Predicate.Like>().Match.ShouldBe(LikeMatch.StartsWith);
    }

    [Fact]
    public void GivenAContainsTextModifiedToken_WhenLowered_ThenUsesAContainsLikeMatch()
    {
        // Arrange -- Observation?code:text:contains=aux binds to a StringExpression with StringOperator.Contains.
        // The Contains arm of the operator switch is distinct from StartsWith and would silently regress if
        // mis-mapped, so it is pinned separately.
        var parameter = CodeParameter();
        var tree = new SearchParameterExpression(
            parameter,
            new StringExpression(StringOperator.Contains, FieldName.TokenText, componentIndex: null, "aux", ignoreCase: true));

        // Act
        var plan = Lower.Run(tree, Symbols(parameter), targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var source = plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ParamSource>();
        source.Table.TableName.ShouldBe("TokenText");
        source.Predicate.ShouldBeOfType<Predicate.Like>().Match.ShouldBe(LikeMatch.Contains);
    }

    [Fact]
    public void GivenATextModifiedToken_WhenEmitted_ThenEmitsNoCollateBecauseTheColumnIsAlreadyCaseInsensitive()
    {
        // Arrange -- dbo.TokenText.Text is declared Latin1_General_CI_AI, so :text gets its case- and
        // accent-insensitive match from the column's own collation. Forcing a COLLATE would make the
        // predicate non-sargable against that table's index, so the rule deliberately emits none.
        var parameter = CodeParameter();
        var tree = new SearchParameterExpression(
            parameter,
            new StringExpression(StringOperator.StartsWith, FieldName.TokenText, componentIndex: null, "aux", ignoreCase: true));
        var plan = Lower.Run(tree, Symbols(parameter), targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("COLLATE");
    }

    [Fact]
    public void GivenATextModifiedToken_WhenEmitted_ThenExcludesHistoryRowsAndBindsTheValue()
    {
        // Arrange -- unlike every other leaf table, dbo.TokenText carries its own IsHistory column, so a
        // query against it that does not filter history would match superseded versions of a resource.
        var parameter = CodeParameter();
        var tree = new SearchParameterExpression(
            parameter,
            new StringExpression(StringOperator.StartsWith, FieldName.TokenText, componentIndex: null, "aux", ignoreCase: true));
        var plan = Lower.Run(tree, Symbols(parameter), targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("FROM dbo.TokenText");
        emitted.Sql.ShouldContain("IsHistory = 0");
        emitted.Sql.ShouldNotContain("aux");
        // The prefix wildcard belongs in the bound value, not the SQL text -- same as every other LIKE.
        emitted.Parameters.ShouldHaveSingleItem().Value.ShouldBe("aux%");
    }
}
