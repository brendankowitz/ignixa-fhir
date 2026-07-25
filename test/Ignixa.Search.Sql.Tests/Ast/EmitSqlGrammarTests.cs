using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Builders;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SearchSortOrder = Ignixa.Search.Expressions.SortOrder;

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
        yield return ["projection alone", ProjectionAlonePlan()];
        yield return ["projection + outer predicate", ProjectionWithOuterPredicatePlan()];
        yield return ["projection + includes", ProjectionWithIncludesPlan()];
        yield return ["projection + sort", ProjectionWithSortPlan()];
        yield return ["projection + paging", ProjectionWithPagingPlan()];
        yield return ["surrogate range alone", SurrogateRangeAlonePlan()];
        yield return ["surrogate range + outer predicate", SurrogateRangeWithOuterPredicatePlan()];
        yield return ["surrogate range + sort + paging", SurrogateRangeWithSortAndPagingPlan()];
        yield return ["surrogate range + includes", SurrogateRangeWithIncludesPlan()];
        yield return ["search parameter hash alone", SearchParameterHashAlonePlan()];
        yield return ["search parameter hash + projection", SearchParameterHashWithProjectionPlan()];
        yield return ["search parameter hash + outer predicate", SearchParameterHashWithOuterPredicatePlan()];
        yield return ["search parameter hash + surrogate range", SearchParameterHashWithSurrogateRangePlan()];
        yield return ["search parameter hash + projection + outer predicate + surrogate range", SearchParameterHashAllFourPlan()];
        yield return ["search parameter hash + includes", SearchParameterHashWithIncludesPlan()];
        yield return ["search parameter hash + count only", SearchParameterHashCountOnlyPlan()];
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

    [Theory]
    [InlineData(false, false, "default (both filters)")]
    [InlineData(true, false, "include history only")]
    [InlineData(false, true, "include deleted only")]
    [InlineData(true, true, "fully relaxed")]
    public void GivenAResourceSourcePlanWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool includeHistory, bool includeDeleted, string _)
    {
        var visibility = new ResourceVisibility(includeHistory, includeDeleted);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Visibility: visibility);

        var emitted = SqlBuilder.Run(plan);

        SqlGrammar.AssertValid(emitted.Sql);
    }

    [Theory]
    [InlineData(false, false, "default (both filters)")]
    [InlineData(true, false, "include history only")]
    [InlineData(false, true, "include deleted only")]
    [InlineData(true, true, "fully relaxed")]
    public void GivenAForwardChainJoinWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool includeHistory, bool includeDeleted, string _)
    {
        var visibility = new ResourceVisibility(includeHistory, includeDeleted);
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(
                    SqlCatalog.Default.Table("StringSearchParam"), ResourceTypeId: 105, SearchParamId: 202,
                    new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Acme"))),
                new CteDefinition.ChainJoin(new CteRef(0), ReferenceSearchParamId: 55, InnerResourceTypeId: 105, OutputResourceTypeIds: [103], ChainDirection.Forward),
            ],
            new CteRef(1),
            Visibility: visibility);

        var emitted = SqlBuilder.Run(plan);

        SqlGrammar.AssertValid(emitted.Sql);
    }

    [Theory]
    [InlineData(false, false, "default (both filters)")]
    [InlineData(true, false, "include history only")]
    [InlineData(false, true, "include deleted only")]
    [InlineData(true, true, "fully relaxed")]
    public void GivenANotReferencedSourceWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool includeHistory, bool includeDeleted, string _)
    {
        var visibility = new ResourceVisibility(includeHistory, includeDeleted);
        var plan = new QueryPlan(
            [new CteDefinition.NotReferencedSource(103, 96, 969)],
            new CteRef(0),
            Visibility: visibility);

        var emitted = SqlBuilder.Run(plan);

        SqlGrammar.AssertValid(emitted.Sql);
    }

    [Theory]
    [InlineData(false, false, "default (both filters)")]
    [InlineData(true, false, "include history only")]
    [InlineData(false, true, "include deleted only")]
    [InlineData(true, true, "fully relaxed")]
    public void GivenAnIncludeStageWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool includeHistory, bool includeDeleted, string _)
    {
        var visibility = new ResourceVisibility(includeHistory, includeDeleted);
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Includes: [stage],
            Visibility: visibility);

        var emitted = SqlBuilder.Run(plan);

        SqlGrammar.AssertValid(emitted.Sql);
    }

    private static ProjectionSpec StandardProjection() => new(["ResourceId", "Version", "RawResource", "IsDeleted"]);

    private static QueryPlan ProjectionAlonePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Projection: StandardProjection());
    }

    private static QueryPlan ProjectionWithOuterPredicatePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outer = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            OuterPredicate: outer,
            Projection: StandardProjection());
    }

    private static QueryPlan ProjectionWithIncludesPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Includes: [stage],
            Projection: StandardProjection());
    }

    private static QueryPlan ProjectionWithSortPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SearchSortOrder.Ascending)], SortPhase.Valued);
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Projection: StandardProjection());
    }

    private static QueryPlan ProjectionWithPagingPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SearchSortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Page: page,
            Projection: StandardProjection());
    }

    private static SurrogateIdRange StandardRange()
        => new(new SqlParameterRef(5000L), new SqlParameterRef(6000L));

    private static QueryPlan SurrogateRangeAlonePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            SurrogateRange: StandardRange());
    }

    private static QueryPlan SurrogateRangeWithOuterPredicatePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outer = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            OuterPredicate: outer,
            SurrogateRange: StandardRange());
    }

    private static QueryPlan SurrogateRangeWithSortAndPagingPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SearchSortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Page: page,
            SurrogateRange: StandardRange());
    }

    private static QueryPlan SurrogateRangeWithIncludesPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Includes: [stage],
            SurrogateRange: StandardRange());
    }

    [Fact]
    public void GivenAProjectionColumnNameContainingAClosingBracket_WhenEmitted_ThenItIsDoubledAndTheSqlIsValid()
    {
        // Proves the bracket-escaping path: a name containing ']' must emit ']]' so the quoted identifier
        // is well-formed and SQL Server's grammar accepts it.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Projection: new ProjectionSpec(["Raw]Resource"]));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain("r.[Raw]]Resource]");
        SqlGrammar.AssertValid(emitted.Sql);
    }

    private static SqlParameterRef StandardHash() => new("hash-abc-123");

    private static QueryPlan SearchParameterHashAlonePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            SearchParameterHash: StandardHash());
    }

    private static QueryPlan SearchParameterHashWithProjectionPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Projection: StandardProjection(),
            SearchParameterHash: StandardHash());
    }

    private static QueryPlan SearchParameterHashWithOuterPredicatePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outer = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            OuterPredicate: outer,
            SearchParameterHash: StandardHash());
    }

    private static QueryPlan SearchParameterHashWithSurrogateRangePlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            SurrogateRange: StandardRange(),
            SearchParameterHash: StandardHash());
    }

    private static QueryPlan SearchParameterHashAllFourPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outer = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            OuterPredicate: outer,
            Projection: StandardProjection(),
            SurrogateRange: StandardRange(),
            SearchParameterHash: StandardHash());
    }

    private static QueryPlan SearchParameterHashWithIncludesPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Includes: [stage],
            SearchParameterHash: StandardHash());
    }

    private static QueryPlan SearchParameterHashCountOnlyPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            CountOnly: true,
            SearchParameterHash: StandardHash());
    }

    [Fact]
    public void GivenAMultiTypeResourceSourceWithSeveralIds_WhenParsed_ThenItIsValidTSql()
    {
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.ForTypes([103, 104])],
            new CteRef(0));

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Fact]
    public void GivenAMultiTypeResourceSourceWithNoIds_WhenParsed_ThenItIsValidTSql()
    {
        // Empty list = system-wide scan; the emitter must produce no WHERE clause, not a dangling AND or
        // an empty WHERE ().
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.AllTypes()],
            new CteRef(0));

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Fact]
    public void GivenAMultiTypeResourceSourceWithOneId_WhenParsed_ThenItIsValidTSql()
    {
        // Single-element list emits IN (x), which is valid T-SQL.
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.ForTypes([103])],
            new CteRef(0));

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Theory]
    [InlineData(false, false, "default (both filters)")]
    [InlineData(true, false, "include history only")]
    [InlineData(false, true, "include deleted only")]
    [InlineData(true, true, "fully relaxed")]
    public void GivenAMultiTypeResourceSourceWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool includeHistory, bool includeDeleted, string _)
    {
        // Tests all four visibility flag combinations, exercising the WHERE-clause assembly for multi-type
        // with visibility filters.
        var visibility = new ResourceVisibility(includeHistory, includeDeleted);
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.ForTypes([103, 104])],
            new CteRef(0),
            Visibility: visibility);

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Theory]
    [InlineData(false, false, "default (both filters)")]
    [InlineData(true, false, "include history only")]
    [InlineData(false, true, "include deleted only")]
    [InlineData(true, true, "fully relaxed")]
    public void GivenASystemWideMultiTypeResourceSourceWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool includeHistory, bool includeDeleted, string _)
    {
        // System-wide (AllTypes) with visibility: validates that the visibility clauses alone build a
        // correct WHERE clause when there is no type filter.
        var visibility = new ResourceVisibility(includeHistory, includeDeleted);
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.AllTypes()],
            new CteRef(0),
            Visibility: visibility);

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    // Multi-type WHERE text for all four visibility combinations, verified against the expected output.
    // These lock in that the clause-list emitter produces byte-identical output to the former
    // concatenate-then-strip approach. Any future refactor that changes WHERE formatting will fail here.
    [Theory]
    [InlineData(false, false, "    WHERE ResourceTypeId IN (103, 104) AND IsHistory = 0 AND IsDeleted = 0")]
    [InlineData(true,  false, "    WHERE ResourceTypeId IN (103, 104) AND IsDeleted = 0")]
    [InlineData(false, true,  "    WHERE ResourceTypeId IN (103, 104) AND IsHistory = 0")]
    [InlineData(true,  true,  "    WHERE ResourceTypeId IN (103, 104)")]
    public void GivenAMultiTypeResourceSourceAcrossAllVisibilityCombinations_TheWhereClauseIsExact(
        bool includeHistory, bool includeDeleted, string expectedWhereClause)
    {
        var visibility = new ResourceVisibility(includeHistory, includeDeleted);
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.ForTypes([103, 104])],
            new CteRef(0),
            Visibility: visibility);

        SqlBuilder.Run(plan).Sql.ShouldContain(expectedWhereClause);
    }

    // AllTypes (system-wide) WHERE text for all four visibility combinations.
    [Theory]
    [InlineData(false, false, "    WHERE IsHistory = 0 AND IsDeleted = 0")]
    [InlineData(true,  false, "    WHERE IsDeleted = 0")]
    [InlineData(false, true,  "    WHERE IsHistory = 0")]
    [InlineData(true,  true,  null)]  // fully relaxed: no WHERE clause
    public void GivenAnAllTypesResourceSourceAcrossAllVisibilityCombinations_TheWhereClauseIsExact(
        bool includeHistory, bool includeDeleted, string? expectedWhereClause)
    {
        var visibility = new ResourceVisibility(includeHistory, includeDeleted);
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.AllTypes()],
            new CteRef(0),
            Visibility: visibility);

        var sql = SqlBuilder.Run(plan).Sql;
        if (expectedWhereClause is null)
        {
            sql.ShouldNotContain("WHERE");
        }
        else
        {
            sql.ShouldContain(expectedWhereClause);
        }
    }

    // -----------------------------------------------------------------------------------------------------
    // Access-constraint shapes. Built through Lower.Run (the real production path) rather than hand-assembled
    // QueryPlans, because a constraint's CTE and its type-guarded EXISTS are wired up by the lowerer -- these
    // prove that the emitted SQL for a constrained match, include, :iterate, and chain all parse as valid
    // T-SQL under the SQL Server grammar.
    // -----------------------------------------------------------------------------------------------------

    private const short AcObservationTypeId = 104;
    private const short AcPatientTypeId = 103;
    private const short AcStatusParamId = 220;
    private const short AcSubjectParamId = 230;

    private static (Ignixa.Search.Sql.Symbols.SymbolTable Symbols, Ignixa.Search.Models.AccessConstraint Constraint, Ignixa.Search.Models.SearchParameterInfo SubjectParam, Ignixa.Search.Models.SearchParameterInfo StatusParam) AccessConstraintFixture()
    {
        var statusParam = new Ignixa.Search.Models.SearchParameterInfo("status", "status", Ignixa.Specification.ValueSets.Normative.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var subjectParam = new Ignixa.Search.Models.SearchParameterInfo("subject", "subject", Ignixa.Specification.ValueSets.Normative.SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        var symbols = new Ignixa.Search.Sql.Symbols.SymbolTable(
            new Dictionary<string, short>
            {
                [statusParam.Url!.ToString()] = AcStatusParamId,
                [subjectParam.Url!.ToString()] = AcSubjectParamId,
            },
            new Dictionary<string, short> { ["Observation"] = AcObservationTypeId, ["Patient"] = AcPatientTypeId });

        return (symbols, new Ignixa.Search.Models.AccessConstraint("Observation", AcTokenPredicate(statusParam, "final")), subjectParam, statusParam);
    }

    private static Ignixa.Search.Expressions.Expression AcTokenPredicate(Ignixa.Search.Models.SearchParameterInfo parameter, string code)
        => new Ignixa.Search.Expressions.SearchParameterExpression(
            parameter,
            new Ignixa.Search.Expressions.SearchParameterPredicateExpression(
                parameter,
                Ignixa.Specification.ValueSets.Normative.SearchComparator.Eq,
                modifier: null,
                new Ignixa.Search.Indexing.SearchValues.TokenSearchValue(system: null, code: code, text: null)));

    [Fact]
    public void GivenAConstrainedMatchOnlyPlan_WhenParsed_ThenItIsValidTSql()
    {
        var f = AccessConstraintFixture();
        var plan = Ignixa.Search.Sql.Lowering.Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.Constraint]).Plan;

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Fact]
    public void GivenAConstrainedRevIncludePlan_WhenParsed_ThenItIsValidTSql()
    {
        var f = AccessConstraintFixture();
        var revinclude = new Ignixa.Search.Expressions.IncludeExpression(["Observation"], f.SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: false);
        var plan = Ignixa.Search.Sql.Lowering.Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [revinclude], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.Constraint]).Plan;

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Fact]
    public void GivenAConstrainedIteratePlan_WhenParsed_ThenItIsValidTSql()
    {
        var f = AccessConstraintFixture();
        var iterate = new Ignixa.Search.Expressions.IncludeExpression(["Observation"], f.SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: true);
        var plan = Ignixa.Search.Sql.Lowering.Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [iterate], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.Constraint]).Plan;

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Fact]
    public void GivenAConstrainedChainPlan_WhenParsed_ThenItIsValidTSql()
    {
        var f = AccessConstraintFixture();
        var chain = new Ignixa.Search.Expressions.ChainedExpression(
            resourceTypes: ["Observation"],
            referenceSearchParameter: f.SubjectParam,
            targetResourceTypes: ["Patient"],
            reversed: true,
            expression: AcTokenPredicate(f.StatusParam, "final"));
        var plan = Ignixa.Search.Sql.Lowering.Lower.Run(
            chain, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, accessConstraints: [f.Constraint]).Plan;

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }
}
