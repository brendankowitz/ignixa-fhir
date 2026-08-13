using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Tests.Builders;

/// <summary>
/// The emitters used to interpolate their own indentation, newlines and separators. These pin the properties
/// that made that error-prone, now that one renderer owns the layout.
/// </summary>
public class SelectBlockTests
{
    [Fact]
    public void GivenNoWhereClauses_WhenRendered_ThenItEmitsNoWhereAndNoTrailingNewline()
    {
        // The failure this prevents: an emitter that appended "\n" after FROM and then interpolated an empty
        // WHERE left a dangling blank line, and one that interpolated "WHERE " with no clauses emitted SQL
        // that does not parse. Neither is representable now -- an empty clause list simply emits no WHERE.
        var sql = new SelectBlock { Columns = "T1, Sid1", From = "dbo.Resource" }.Render();

        sql.ShouldBe("    SELECT T1, Sid1\n    FROM dbo.Resource");
        sql.ShouldNotEndWith("\n");
        sql.ShouldNotContain("WHERE");
    }

    [Fact]
    public void GivenInlineLayout_WhenRendered_ThenClausesShareOneLine()
    {
        var sql = new SelectBlock
        {
            Distinct = true,
            Columns = "T1",
            From = "dbo.Resource",
            Where = ["A = 1", "B = 2"],
        }.Render();

        sql.ShouldBe("    SELECT DISTINCT T1\n    FROM dbo.Resource\n    WHERE A = 1 AND B = 2");
    }

    [Fact]
    public void GivenStackedLayout_WhenRendered_ThenEachClauseTakesItsOwnIndentedLine()
    {
        var sql = new SelectBlock
        {
            Columns = "T1",
            From = "dbo.Resource",
            WhereLayout = WhereLayout.Stacked,
            Where = ["A = 1", "B = 2"],
        }.Render();

        sql.ShouldBe("    SELECT T1\n    FROM dbo.Resource\n    WHERE A = 1\n      AND B = 2");
    }

    [Fact]
    public void GivenTopJoinsOrderByAndOffset_WhenRendered_ThenTheyAppearInStatementOrder()
    {
        var sql = new SelectBlock
        {
            Top = 10,
            Columns = "T1",
            From = "cte0 m",
            Joins = ["    INNER JOIN cte1 x ON x.T1 = m.T1"],
            Where = ["A = 1"],
            OrderBy = "m.Sid1 ASC",
            Offset = "OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY",
        }.Render();

        sql.ShouldBe(
            "    SELECT TOP (10) T1\n" +
            "    FROM cte0 m\n" +
            "    INNER JOIN cte1 x ON x.T1 = m.T1\n" +
            "    WHERE A = 1\n" +
            "    ORDER BY m.Sid1 ASC\n" +
            "    OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY");
    }

    [Fact]
    public void GivenAnUnconstrainedMultiTypeScan_WhenEmitted_ThenItsCteBodyHasNoDanglingBlankLine()
    {
        // Regression: EmitMultiTypeResourceSource appended its WHERE without a leading newline and so had to
        // put one after FROM, leaving "FROM dbo.Resource\n\n)" when every clause was absent -- the only
        // emitter that did it that way round. No golden covered the unconstrained case, so it went unnoticed.
        var plan = new QueryPlan(
            [CteDefinition.MultiTypeResourceSource.AllTypes()],
            new MatchPageSpec(new CteRef(0)),
            Visibility: new ResourceVisibility(IsHistory: null, IsDeleted: null));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("    FROM dbo.Resource\n)");
        sql.ShouldNotContain("\n\n");
    }
}
