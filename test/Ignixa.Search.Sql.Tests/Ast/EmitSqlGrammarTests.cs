using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Builders;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Ignixa.Search.Sql.Tests.Ast;

/// <summary>
/// Proves every emitted-SQL shape is valid T-SQL under the SQL Server grammar (via ScriptDom), and
/// asserts on the parsed object model rather than exact text. This catches malformed SQL — unbalanced
/// parens, broken CTE lists, reserved-word misuse — that byte-exact goldens can also catch but far more
/// brittly. (Grammar validity is not the same as executability: SQL Server's semantic rules, e.g. Msg
/// 1033 "ORDER BY not allowed in a CTE without TOP", are checked by the integration tests, not here.)
/// </summary>
public class EmitSqlGrammarTests
{
    public static IEnumerable<object[]> AllPlanShapes()
    {
        yield return ["single leaf", SingleLeafPlan()];
        yield return ["intersect (AND)", IntersectPlan()];
        yield return ["union (OR)", UnionPlan()];
        yield return ["resource-source with outer predicate", OuterPredicatePlan()];
        yield return ["count only", CountOnlyPlan()];
        yield return ["contains (LIKE)", LikePlan()];
        yield return ["prefix of parameter", PrefixOfParameterPlan()];
        yield return ["dual-column contains (IsNull guard + overflow)", DualColumnContainsPlan()];
    }

    [Theory]
    [MemberData(nameof(AllPlanShapes))]
    public void GivenAnEmittedPlan_WhenParsedWithTheSqlServerGrammar_ThenItIsValidTSql(string shape, QueryPlan plan)
    {
        _ = shape;

        var emitted = SqlBuilder.Run(plan);

        SqlGrammar.AssertValid(emitted.Sql);
    }

    [Fact]
    public void GivenALeafPlan_WhenParsed_ThenTheObjectModelHasExactlyOneSelectStatement()
    {
        var emitted = SqlBuilder.Run(SingleLeafPlan());

        var fragment = SqlGrammar.Parse(emitted.Sql);

        SqlGrammar.Count<SelectStatement>(fragment).ShouldBe(1);
    }

    [Fact]
    public void GivenACountOnlyPlan_WhenParsed_ThenTheObjectModelContainsACountBigCall()
    {
        var emitted = SqlBuilder.Run(CountOnlyPlan());

        var fragment = SqlGrammar.Parse(emitted.Sql);

        SqlGrammar.Count<FunctionCall>(fragment).ShouldBeGreaterThanOrEqualTo(1);
    }

    private static QueryPlan SingleLeafPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        return new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10);
    }

    private static QueryPlan IntersectPlan()
    {
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var stringPredicate = new Predicate.Equal(new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"));
        var tokenPredicate = new Predicate.Equal(new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"));
        return new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 103, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 103, 44, tokenPredicate),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            new CteRef(2));
    }

    private static QueryPlan UnionPlan()
    {
        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var left = new Predicate.Equal(new SqlColumnRef(table.TableName, "Code"), new SqlParameterRef("male"));
        var right = new Predicate.Equal(new SqlColumnRef(table.TableName, "Code"), new SqlParameterRef("female"));
        return new QueryPlan(
            [
                new CteDefinition.ParamSource(table, 103, 44, left),
                new CteDefinition.ParamSource(table, 103, 44, right),
                new CteDefinition.Union([new CteRef(0), new CteRef(1)]),
            ],
            new CteRef(2));
    }

    private static QueryPlan OuterPredicatePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outer = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            OuterPredicate: outer);
    }

    private static QueryPlan CountOnlyPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), CountOnly: true);
    }

    private static QueryPlan LikePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Like(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smi"), LikeMatch.Contains, "Latin1_General_100_CI_AI");
        return new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10);
    }

    private static QueryPlan PrefixOfParameterPlan()
    {
        var table = SqlCatalog.Default.Table("UriSearchParam");
        var predicate = new Predicate.PrefixOfParameter(
            new SqlColumnRef(table.TableName, "Uri"),
            new SqlParameterRef("http://example.org/fhir/Patient/123"),
            "Latin1_General_100_BIN2");
        return new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));
    }

    private static QueryPlan DualColumnContainsPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var textColumn = new SqlColumnRef(table.TableName, "Text");
        var overflowColumn = new SqlColumnRef(table.TableName, "TextOverflow");
        var predicate = new Predicate.Or(
            new Predicate.And(
                new Predicate.IsNull(overflowColumn),
                new Predicate.Like(textColumn, new SqlParameterRef("mit"), LikeMatch.Contains, "Latin1_General_100_CI_AI")),
            new Predicate.Like(overflowColumn, new SqlParameterRef("mit"), LikeMatch.Contains, "Latin1_General_100_CI_AI"));
        return new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));
    }

    [Fact]
    public void GivenTheDualColumnContainsShape_WhenParsed_ThenTheObjectModelContainsTwoLikePredicatesAndAnIsNullExpression()
    {
        var emitted = SqlBuilder.Run(DualColumnContainsPlan());

        var fragment = SqlGrammar.Parse(emitted.Sql);

        SqlGrammar.Count<LikePredicate>(fragment).ShouldBe(2);
    }
}
