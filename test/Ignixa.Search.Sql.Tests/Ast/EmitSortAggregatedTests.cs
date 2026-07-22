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
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- key 0 in Valued phase is an INNER join (gates on the key being present, matching
        // String/Date's own Valued-phase gating -- an aggregated key is no different: SortSpec's own
        // contract is "Valued makes Keys[0]'s join INNER," not "String/Date's join is INNER." A LEFT
        // join here would let missing-key rows leak into both phases (duplicates across the keyset
        // page boundary) and let a NULL AggValue flow unwrapped into the seek predicate, silently
        // dropping later-page rows -- this is why key 0 is INNER, not just "guaranteed non-null so we
        // skip the ISNULL wrapper." The derived table can be INNER-joined directly (no separate
        // existence check needed) since MIN/MAX over zero grouped rows simply produces zero output rows
        // for that key, which is exactly INNER JOIN's semantics.
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
            "ORDER BY sk0.AggValue ASC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenADescendingNumberSortKeyAsASecondaryKey_WhenEmitted_ThenTheDerivedTableIsLeftJoinedAndAggregatesWithMax()
    {
        // Arrange -- a secondary (index 1) sort key is always a LEFT-JOIN tie-breaker regardless of
        // kind (matches String/Date's own existing i==0 ? INNER : LEFT rule) -- unlike index 0, a
        // missing secondary key must NOT exclude the row, so this case legitimately stays LEFT.
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
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- NumberSearchParam.LowValue is DECIMAL(36,18) (real DDL:
        // src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/NumberSearchParam.sql), so its
        // ISNULL sentinel is the unquoted numeric literal 0, not a string sentinel.
        emitted.Sql.ShouldContain("MAX(LowValue) AS AggValue");
        emitted.Sql.ShouldContain("LEFT JOIN (\n    SELECT ResourceTypeId, ResourceSurrogateId, MAX(LowValue) AS AggValue");
        emitted.Sql.ShouldContain("ISNULL(sk1.AggValue, 0)");
    }

    [Fact]
    public void GivenTheMissingPrimaryPhaseWithAnAggregatedPrimaryKey_WhenEmitted_ThenNoJoinIsEmittedAndNotExistsGuardsInstead()
    {
        // Arrange -- proves the phase-transition contract holds for Aggregated exactly like String/Date:
        // MissingPrimary excludes key 0 from EmitSortJoins entirely (the existing "if (i == 0 &&
        // sort.Phase == SortPhase.MissingPrimary) continue;" guard, unchanged) and instead requires
        // absence via NOT EXISTS. This is a regression guard: without the join-type fix, a bug that
        // makes key 0 unconditionally LEFT-joined would silently duplicate rows across the two phases
        // and nothing else in this test file would catch it (proven by temporarily reverting the fix --
        // see task-2-report.md).
        var predicateTable = SqlCatalog.Default.Table("TokenSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(predicateTable.TableName, "Code"), new SqlParameterRef("final"));
        var sortTable = SqlCatalog.Default.Table("TokenSearchParam");
        var sortColumn = sortTable.Column("Code");
        var sort = new SortSpec(
            [new SortKey(77, SortKeyKind.Aggregated, SortOrder.Ascending, sortTable, sortColumn)],
            SortPhase.MissingPrimary);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("sk0");
        emitted.Sql.ShouldContain("NOT EXISTS (SELECT 1 FROM dbo.TokenSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 77)");
    }
}
