// Two SortOrder enums exist in this codebase (Ignixa.Search.Expressions and Ignixa.Search.Indexing) --
// this using brings Expressions' SortOrder into scope unambiguously.
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class EmitSortAggregatedTests
{
    [Fact]
    public void GivenASingleAscendingTokenSortKeyInTheValuedPhase_WhenEmitted_ThenInnerJoinsAnAggregatingDerivedTable()
    {
        // Arrange -- Observation?_sort=status, first page (no boundary).
        var predicateTable = SqlCatalog.Default.Table("TokenSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(predicateTable.TableName, "Code"), new SqlParameterRef("final"));
        var sortTable = SqlCatalog.Default.Table("TokenSearchParam");
        var sortColumn = sortTable.Column("Code");
        var sort = new SortSpec(
            [new SortKey(77, SortKeyKind.Aggregated, SortOrder.Ascending, sortTable, sortColumn)],
            SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)], new MatchPageSpec(new CteRef(0), Top: 10, Sort: sort));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- key 0 in the Valued phase is an INNER join, so it also gates on the key being
        // present, exactly like String/Date's own i==0-is-INNER rule. A LEFT join here would let
        // missing-key rows leak into both the Valued and MissingPrimary phases (duplicates across the
        // keyset page boundary) and let a NULL AggValue reach the seek predicate unwrapped. INNER
        // against the derived table needs no separate existence check: MIN/MAX over zero grouped rows
        // for a given (type, surrogate id) simply produces no output row, which is INNER's semantics.
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Code = @p0\n" +
            ")\n" +
            "SELECT TOP (10) m.T1, m.Sid1, sk0.AggValue AS SortValue0 FROM cte0 m\n" +
            "INNER JOIN (\n" +
            "    SELECT ResourceTypeId, ResourceSurrogateId, MIN(Code) AS AggValue\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE SearchParamId = 77\n" +
            "    GROUP BY ResourceTypeId, ResourceSurrogateId\n" +
            ") sk0 ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "ORDER BY sk0.AggValue ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenADescendingNumberSortKeyAsASecondaryKey_WhenEmitted_ThenTheDerivedTableIsLeftJoinedAndAggregatesWithMax()
    {
        // Arrange -- a secondary (index 1) sort key is always a LEFT-JOIN tie-breaker regardless of
        // kind (matching String/Date's own i==0 ? INNER : LEFT rule) -- unlike index 0, a missing
        // secondary key must NOT exclude the row, so this case legitimately stays LEFT.
        var predicateTable = SqlCatalog.Default.Table("NumberSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(predicateTable.TableName, "LowValue"), new SqlParameterRef(5m));
        var secondaryTable = SqlCatalog.Default.Table("NumberSearchParam");
        var secondaryColumn = secondaryTable.Column("LowValue");
        var sort = new SortSpec(
            [
                new SortKey(50, SortKeyKind.String, SortOrder.Ascending),
                new SortKey(88, SortKeyKind.Aggregated, SortOrder.Descending, secondaryTable, secondaryColumn),
            ],
            SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)], new MatchPageSpec(new CteRef(0), Top: 10, Sort: sort));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- NumberSearchParam.LowValue is DECIMAL(36,18) in the schema DDL, so its ISNULL
        // sentinel is the unquoted numeric literal 0, not a string sentinel.
        emitted.Sql.ShouldContain("MAX(LowValue) AS AggValue");
        emitted.Sql.ShouldContain("LEFT JOIN (\n    SELECT ResourceTypeId, ResourceSurrogateId, MAX(LowValue) AS AggValue");
        emitted.Sql.ShouldContain("ISNULL(sk1.AggValue, 0)");
    }

    [Fact]
    public void GivenTheMissingPrimaryPhaseWithAnAggregatedPrimaryKey_WhenEmitted_ThenNoJoinIsEmittedAndNotExistsGuardsInstead()
    {
        // Arrange -- proves the phase-transition contract holds for Aggregated exactly like String/Date:
        // MissingPrimary excludes key 0 from EmitSortJoins entirely and instead requires absence via
        // NOT EXISTS. The regression guard against an unconditional-LEFT-JOIN bug (which would duplicate
        // rows across the two phases) is the Valued-phase test above; this one validates only that
        // MissingPrimary skips the join altogether.
        var predicateTable = SqlCatalog.Default.Table("TokenSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(predicateTable.TableName, "Code"), new SqlParameterRef("final"));
        var sortTable = SqlCatalog.Default.Table("TokenSearchParam");
        var sortColumn = sortTable.Column("Code");
        var sort = new SortSpec(
            [new SortKey(77, SortKeyKind.Aggregated, SortOrder.Ascending, sortTable, sortColumn)],
            SortPhase.MissingPrimary);
        var plan = new QueryPlan([new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)], new MatchPageSpec(new CteRef(0), Top: 10, Sort: sort));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("sk0");
        emitted.Sql.ShouldContain("NOT EXISTS (SELECT 1 FROM dbo.TokenSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 77)");
    }

    [Fact]
    public void GivenAResourceIdSortKey_WhenEmitted_ThenJoinsResourceDirectlyAndOrdersByItsResourceIdColumn()
    {
        // Arrange -- Patient?_sort=_id. The CTE graph's own (T1, Sid1) projection carries no ResourceId
        // string, so unlike _lastUpdated this resource-column key still needs a join -- to dbo.Resource,
        // not to a search-param table.
        var predicateTable = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(predicateTable.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(null, SortKeyKind.ResourceId, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)], new MatchPageSpec(new CteRef(0), Top: 10, Sort: sort));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource rid0 ON rid0.ResourceTypeId = m.T1 AND rid0.ResourceSurrogateId = m.Sid1");
        emitted.Sql.ShouldContain("ORDER BY rid0.ResourceId ASC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenAResourceIdSortKeyAndAnOuterPredicate_WhenEmitted_ThenTheOuterPredicateColumnsAreQualified()
    {
        // Arrange -- Patient?_id=abc&_sort=_id. Two joins onto dbo.Resource land in one statement: the
        // resource join `r` that NeedsResourceJoin adds for the outer predicate, and the `rid0` sort join.
        // Both expose ResourceId, so an unqualified `WHERE ResourceId = @p` binds to neither and SQL Server
        // raises Msg 209 (ambiguous column name). ScriptDom parses it happily -- an ambiguous identifier is
        // grammatically valid -- so only an explicit assertion on the qualifier catches this.
        var outerPredicate = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("abc"));
        var sort = new SortSpec([new SortKey(null, SortKeyKind.ResourceId, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new MatchPageSpec(new CteRef(0), OuterPredicate: outerPredicate, Sort: sort));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- both aliases present, and the outer predicate names the one it means.
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource rid0 ON");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r ON");
        emitted.Sql.ShouldContain("WHERE r.ResourceId = @p1");
        emitted.Sql.ShouldNotContain("WHERE ResourceId = @p1");
    }
}
