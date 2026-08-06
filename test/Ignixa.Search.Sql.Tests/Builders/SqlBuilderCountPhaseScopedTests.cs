using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Builders;

public class SqlBuilderCountPhaseScopedTests
{
    [Fact]
    public void GivenCountPhaseScopedTrueOnAValuedPhasePlan_WhenEmitted_ThenTheCountQueryJoinsTheSortKey()
    {
        // Arrange -- Patient?_sort=name, count-only, phase-scoped to the Valued phase.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0),
            Sort: sort, Shape: new ResultShape.Count.CurrentSortPhase());

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the count query must join the sort key's table (proving it's phase-scoped, not the
        // whole match set), matching the same join shape EmitSortJoins renders for the non-count path.
        emitted.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1)");
        emitted.Sql.ShouldContain("JOIN");
        emitted.Sql.ShouldNotContain("ORDER BY");
        emitted.Sql.ShouldNotContain("OFFSET");
    }

    [Fact]
    public void GivenCountPhaseScopedFalse_WhenEmittedAlongsideASort_ThenTheCountQueryIsUnaffectedByTheSort()
    {
        // Regression guard: proves this task did NOT change unscoped CountOnly's existing behavior --
        // _total=accurate & _sort=X (Phase 9's own tested composition) must still report the TRUE total
        // match count, ignoring sort entirely, exactly as before this task.

        // Arrange -- same sorted plan as above, but countPhaseScoped left at its default (false).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0),
            Sort: sort, Shape: new ResultShape.Count.AllMatches());

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- no sort-key join appears; this is the exact rendering CountOnly has always produced.
        emitted.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1)");
        emitted.Sql.ShouldNotContain("JOIN");
    }
}
