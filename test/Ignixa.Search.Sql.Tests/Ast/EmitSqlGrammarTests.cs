using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
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
        yield return ["typeless multi-type paging", TypelessPagingPlan()];
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
        yield return ["includes-only, single stage", IncludesOnlyPlan()];
        yield return ["includes-only, two stages", IncludesOnlyTwoStagesPlan()];
        yield return ["includes-only, :iterate", IncludesOnlyWithIteratePlan()];
        yield return ["includes-only, with projection", IncludesOnlyWithProjectionPlan()];
        yield return ["includes-only, page with cursor", IncludesOnlyPageWithCursorPlan()];
        yield return ["includes-only, custom sort (missing-value phase)", IncludesOnlyWithMissingPrimarySortPlan()];
        yield return ["includes-only, custom sort (valued phase)", IncludesOnlyWithValuedSortPlan()];
        yield return ["patient $everything alone", EverythingAlonePlan()];
        yield return ["patient $everything with _since", EverythingWithSincePlan()];
        yield return ["patient $everything with _type", EverythingWithTypePlan()];
        yield return ["patient $everything with projection", EverythingWithProjectionPlan()];
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
    [InlineData(null, null, "no filters")]
    [InlineData(false, false, "current rows only")]
    [InlineData(true, null, "history rows only")]
    [InlineData(null, true, "deleted rows only")]
    [InlineData(true, true, "history and deleted rows only")]
    public void GivenAResourceSourcePlanWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool? isHistory, bool? isDeleted, string _)
    {
        var visibility = new ResourceVisibility(isHistory, isDeleted);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Visibility: visibility);

        var emitted = SqlBuilder.Run(plan);

        SqlGrammar.AssertValid(emitted.Sql);
    }

    [Theory]
    [InlineData(null, null, "no filters")]
    [InlineData(false, false, "current rows only")]
    [InlineData(true, null, "history rows only")]
    [InlineData(null, true, "deleted rows only")]
    [InlineData(true, true, "history and deleted rows only")]
    public void GivenAForwardChainJoinWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool? isHistory, bool? isDeleted, string _)
    {
        var visibility = new ResourceVisibility(isHistory, isDeleted);
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
    [InlineData(null, null, "no filters")]
    [InlineData(false, false, "current rows only")]
    [InlineData(true, null, "history rows only")]
    [InlineData(null, true, "deleted rows only")]
    [InlineData(true, true, "history and deleted rows only")]
    public void GivenANotReferencedSourceWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool? isHistory, bool? isDeleted, string _)
    {
        var visibility = new ResourceVisibility(isHistory, isDeleted);
        var plan = new QueryPlan(
            [new CteDefinition.NotReferencedSource(103, 96, 969)],
            new CteRef(0),
            Visibility: visibility);

        var emitted = SqlBuilder.Run(plan);

        SqlGrammar.AssertValid(emitted.Sql);
    }

    [Theory]
    [InlineData(null, null, "no filters")]
    [InlineData(false, false, "current rows only")]
    [InlineData(true, null, "history rows only")]
    [InlineData(null, true, "deleted rows only")]
    [InlineData(true, true, "history and deleted rows only")]
    public void GivenAnIncludeStageWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool? isHistory, bool? isDeleted, string _)
    {
        var visibility = new ResourceVisibility(isHistory, isDeleted);
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

    /// <summary>
    /// A symbol table whose Patient compartment reaches Observation and Encounter through the "subject"
    /// reference parameter — the shape Resolve produces from an ICompartmentDefinitionManager — so a
    /// $everything search lowers to a real compartment traversal rather than a bare Patient scan. The four
    /// referenced resource types are registered too, matching what SymbolCollectingVisitor collects for an
    /// $everything whose referenced-resource expansion is on (the default).
    /// </summary>
    private static SymbolTable EverythingSymbols(IReadOnlyList<string>? memberTypes = null)
    {
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var membership = new Dictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>
        {
            ["Patient"] = new List<(SearchParameterInfo, IReadOnlyList<string>)> { (subjectParam, memberTypes ?? ["Observation", "Encounter"]) },
        };

        return new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url!.ToString()] = 77 },
            new Dictionary<string, short>
            {
                ["Patient"] = 103,
                ["Observation"] = 104,
                ["Encounter"] = 105,
                ["Practitioner"] = 201,
                ["Organization"] = 202,
                ["Location"] = 203,
                ["Medication"] = 204,
            },
            compartmentMembership: membership);
    }

    private static QueryPlan EverythingPlan(PatientEverythingExpression expression)
        => Lower.Run(expression, EverythingSymbols(), "Patient", includes: [], revIncludes: [], includeLimit: 100, sort: [], SortPhase.Valued, page: null).Plan;

    private static QueryPlan EverythingAlonePlan() => EverythingPlan(new PatientEverythingExpression("pat-1"));

    private static QueryPlan EverythingWithSincePlan() => EverythingPlan(new PatientEverythingExpression("pat-1", sinceDate: new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero)));

    private static QueryPlan EverythingWithTypePlan() => EverythingPlan(new PatientEverythingExpression("pat-1", filteredResourceTypes: new HashSet<string> { "Encounter" }));

    private static QueryPlan EverythingWithProjectionPlan() => EverythingAlonePlan() with { Projection = StandardProjection() };

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

    private static QueryPlan TypelessPagingPlan()
    {
        // A multi-type _sort=name continuation page: the boundary carries no resource type, so the seek
        // and ORDER BY break their final tie on the surrogate id alone. Exercised through the grammar
        // checker to confirm the type-free seek is still valid T-SQL.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SearchSortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Page: page);
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
    [InlineData(null, null, "no filters")]
    [InlineData(false, false, "current rows only")]
    [InlineData(true, null, "history rows only")]
    [InlineData(null, true, "deleted rows only")]
    [InlineData(true, true, "history and deleted rows only")]
    public void GivenAMultiTypeResourceSourceWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool? isHistory, bool? isDeleted, string _)
    {
        // Tests representative visibility combinations, exercising the WHERE-clause assembly for multi-type
        // with visibility filters.
        var visibility = new ResourceVisibility(isHistory, isDeleted);
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.ForTypes([103, 104])],
            new CteRef(0),
            Visibility: visibility);

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Theory]
    [InlineData(null, null, "no filters")]
    [InlineData(false, false, "current rows only")]
    [InlineData(true, null, "history rows only")]
    [InlineData(null, true, "deleted rows only")]
    [InlineData(true, true, "history and deleted rows only")]
    public void GivenASystemWideMultiTypeResourceSourceWithAnyVisibilityCombination_WhenParsed_ThenItIsValidTSql(
        bool? isHistory, bool? isDeleted, string _)
    {
        // System-wide (AllTypes) with visibility: validates that the visibility clauses alone build a
        // correct WHERE clause when there is no type filter.
        var visibility = new ResourceVisibility(isHistory, isDeleted);
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.AllTypes()],
            new CteRef(0),
            Visibility: visibility);

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    // Multi-type WHERE text across the tri-state visibility space, verified against the exact expected
    // output. These lock in that the clause-list emitter renders each column value directly (0/1) and omits
    // a column entirely when its axis is null. One row per row of the ResourceVersionTypes truth table plus
    // the null-axis pins that only the tri-state model can express.
    [Theory]
    [InlineData(null,  null,  "    WHERE ResourceTypeId IN (103, 104)")]
    [InlineData(false, false, "    WHERE ResourceTypeId IN (103, 104) AND IsHistory = 0 AND IsDeleted = 0")]
    [InlineData(true,  null,  "    WHERE ResourceTypeId IN (103, 104) AND IsHistory = 1")]
    [InlineData(null,  true,  "    WHERE ResourceTypeId IN (103, 104) AND IsDeleted = 1")]
    [InlineData(false, null,  "    WHERE ResourceTypeId IN (103, 104) AND IsHistory = 0")]
    [InlineData(null,  false, "    WHERE ResourceTypeId IN (103, 104) AND IsDeleted = 0")]
    [InlineData(true,  true,  "    WHERE ResourceTypeId IN (103, 104) AND IsHistory = 1 AND IsDeleted = 1")]
    public void GivenAMultiTypeResourceSourceAcrossAllVisibilityCombinations_TheWhereClauseIsExact(
        bool? isHistory, bool? isDeleted, string expectedWhereClause)
    {
        var visibility = new ResourceVisibility(isHistory, isDeleted);
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.ForTypes([103, 104])],
            new CteRef(0),
            Visibility: visibility);

        SqlBuilder.Run(plan).Sql.ShouldContain(expectedWhereClause);
    }

    // AllTypes (system-wide) WHERE text across the tri-state visibility space.
    [Theory]
    [InlineData(null,  null,  null)]  // no axis constrained: no WHERE clause at all
    [InlineData(false, false, "    WHERE IsHistory = 0 AND IsDeleted = 0")]
    [InlineData(true,  null,  "    WHERE IsHistory = 1")]
    [InlineData(null,  true,  "    WHERE IsDeleted = 1")]
    [InlineData(false, null,  "    WHERE IsHistory = 0")]
    [InlineData(null,  false, "    WHERE IsDeleted = 0")]
    [InlineData(true,  true,  "    WHERE IsHistory = 1 AND IsDeleted = 1")]
    public void GivenAnAllTypesResourceSourceAcrossAllVisibilityCombinations_TheWhereClauseIsExact(
        bool? isHistory, bool? isDeleted, string? expectedWhereClause)
    {
        var visibility = new ResourceVisibility(isHistory, isDeleted);
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

    private static (SymbolTable Symbols, AccessConstraint Constraint, SearchParameterInfo SubjectParam, SearchParameterInfo StatusParam) AccessConstraintFixture()
    {
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        var symbols = new SymbolTable(
            new Dictionary<string, short>
            {
                [statusParam.Url!.ToString()] = AcStatusParamId,
                [subjectParam.Url!.ToString()] = AcSubjectParamId,
            },
            new Dictionary<string, short> { ["Observation"] = AcObservationTypeId, ["Patient"] = AcPatientTypeId });

        return (symbols, new AccessConstraint("Observation", AcTokenPredicate(statusParam, "final")), subjectParam, statusParam);
    }

    private static Expression AcTokenPredicate(SearchParameterInfo parameter, string code)
        => new SearchParameterExpression(
            parameter,
            new SearchParameterPredicateExpression(
                parameter,
                SearchComparator.Eq,
                modifier: null,
                new TokenSearchValue(system: null, code: code, text: null)));

    [Fact]
    public void GivenAConstrainedMatchOnlyPlan_WhenParsed_ThenItIsValidTSql()
    {
        var f = AccessConstraintFixture();
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, new LowerOptions { AccessConstraints = [f.Constraint] }).Plan;

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Fact]
    public void GivenAConstrainedRevIncludePlan_WhenParsed_ThenItIsValidTSql()
    {
        var f = AccessConstraintFixture();
        var revinclude = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: false);
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [revinclude], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, new LowerOptions { AccessConstraints = [f.Constraint] }).Plan;

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Fact]
    public void GivenAConstrainedIteratePlan_WhenParsed_ThenItIsValidTSql()
    {
        var f = AccessConstraintFixture();
        var iterate = new IncludeExpression(["Observation"], f.SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: true);
        var plan = Lower.Run(
            expression: null, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [iterate], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, new LowerOptions { AccessConstraints = [f.Constraint] }).Plan;

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    [Fact]
    public void GivenAConstrainedChainPlan_WhenParsed_ThenItIsValidTSql()
    {
        var f = AccessConstraintFixture();
        var chain = new ChainedExpression(
            resourceTypes: ["Observation"],
            referenceSearchParameter: f.SubjectParam,
            targetResourceTypes: ["Patient"],
            reversed: true,
            expression: AcTokenPredicate(f.StatusParam, "final"));
        var plan = Lower.Run(
            chain, f.Symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, new LowerOptions { AccessConstraints = [f.Constraint] }).Plan;

        SqlGrammar.AssertValid(SqlBuilder.Run(plan).Sql);
    }

    // ─── IncludesOnly grammar plan factories ────────────────────────────────────────────────────────

    private static QueryPlan IncludesOnlyPlan()
    {
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage],
            IncludesOnly: true);
    }

    private static QueryPlan IncludesOnlyTwoStagesPlan()
    {
        var stage0 = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var stage1 = new IncludeStage(
            IncludeDirection.Reverse, ReferenceSearchParamId: 88, SeedTypeIds: [103], OutputTypeIds: [107],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage0, stage1],
            IncludesOnly: true);
    }

    private static QueryPlan IncludesOnlyWithIteratePlan()
    {
        var stage0 = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var stage1 = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 88, SeedTypeIds: [105], OutputTypeIds: [105],
            SeedStages: [0], SeedFromMatch: true, Iterate: true, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage0, stage1],
            IncludesOnly: true);
    }

    private static QueryPlan IncludesOnlyWithProjectionPlan()
    {
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage],
            Projection: StandardProjection(),
            IncludesOnly: true);
    }

    private static QueryPlan IncludesOnlyPageWithCursorPlan()
    {
        // The $includes second page: two stages of mixed direction, paged globally and resumed from a
        // cursor. Exercises the outer TOP + COUNT_BIG(*) OVER() derived table and the per-stage resume
        // predicate through the ScriptDom grammar so a malformed shape fails here rather than at execution.
        var stage0 = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var stage1 = new IncludeStage(
            IncludeDirection.Reverse, ReferenceSearchParamId: 88, SeedTypeIds: [103], OutputTypeIds: [107],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage0, stage1],
            IncludesOnly: true,
            IncludeCursor: new IncludeCursor(105, 4200));
    }

    private static QueryPlan IncludesOnlyWithMissingPrimarySortPlan()
    {
        // Patient?_sort=date $includes page, missing-value phase. The sort is carried for its filtering role
        // (the NOT EXISTS that bounds the match set to undated rows); it must never reach an ORDER BY. Run
        // through ScriptDom to prove the match-page CTE and the global includes page still parse with the
        // phase filter present but no sort-key ORDER BY or seek.
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage],
            Sort: new SortSpec([new SortKey(203, SortKeyKind.Date, SearchSortOrder.Ascending)], SortPhase.MissingPrimary),
            IncludesOnly: true);
    }

    private static QueryPlan IncludesOnlyWithValuedSortPlan()
    {
        // Same page, valued phase: the phase filter is the primary-key INNER join that bounds the match set to
        // dated rows. The join stays but projects no SortValueN columns; ScriptDom confirms an INNER join whose
        // table is referenced only in the join predicate (not the SELECT list) is still valid T-SQL.
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage],
            Sort: new SortSpec([new SortKey(203, SortKeyKind.Date, SearchSortOrder.Ascending)], SortPhase.Valued),
            IncludesOnly: true);
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithAKeysetPage_WhenEmitted_ThenItIsRefusedRatherThanSeekingTheMatchRowsBySortKey()
    {
        // A sort is now allowed on an includes-only page (its phase filters the match set that seeds the
        // includes), but a keyset Page is not: EmitSeekPredicate would seek the match rows by the sort-key
        // boundary, a second paging mechanism the includes-only page does not use -- its match window is the
        // surrogate range and its include rows page from a cursor. Grammatically valid either way, so the
        // grammar check cannot catch it; the combination is guarded in SqlBuilder.Run the same way
        // IncludesOnly + CountOnly and IncludesOnly + no-stages already are.
        var stage = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [103], OutputTypeIds: [105],
            SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var sort = new SortSpec([new SortKey(203, SortKeyKind.Date, SearchSortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage],
            Sort: sort,
            Page: new PageSpec([new SqlParameterRef("2000-01-01")], BoundaryResourceTypeId: null, BoundarySurrogateId: new SqlParameterRef(4200L)),
            IncludesOnly: true);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
    }
}
