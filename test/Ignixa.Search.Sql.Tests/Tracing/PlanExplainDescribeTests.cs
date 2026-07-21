using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Tracing;

/// <summary>
/// Guards the structured plan rows against the flat text they were extracted from. Dozens of goldens across
/// the compiler suites assert on <see cref="PlanExplainer.Print"/>'s exact output, so the refactor that
/// introduced <see cref="PlanExplainer.Describe"/> is only safe while the two stay one-for-one.
/// </summary>
public class PlanExplainDescribeTests
{
    [Fact]
    public void GivenANonTrivialPlan_WhenPrinted_ThenTheTextIsUnchangedByTheDescribeRefactor()
    {
        // Arrange
        var plan = NonTrivialPlan();

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "cte0 = StringSearchParam[103,202]  Text = @p0\n" +
            "cte1 = TokenSearchParam[103,44]  Code = @p1\n" +
            "root = Intersect(cte0, cte1) WHERE ResourceId = @p2\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)\n" +
            "sort = SortSpec([String:202 ASC], Valued)\n" +
            "page = PageSpec(boundary=[@p3], type=@p4, sid=@p5)");
    }

    [Fact]
    public void GivenANonTrivialPlan_WhenDescribed_ThenEachRowRejoinsToItsOwnPrintedLine()
    {
        // Arrange
        var plan = NonTrivialPlan();

        // Act
        var rows = PlanExplainer.Describe(plan);

        // Assert
        rows.Select(row => $"{row.Label} = {row.Body}").ShouldBe(plan.Explain().Split('\n'));
    }

    [Fact]
    public void GivenANonTrivialPlan_WhenDescribed_ThenTheLabelsNameEveryStageInTraversalOrder()
    {
        // Arrange
        var plan = NonTrivialPlan();

        // Act
        var rows = PlanExplainer.Describe(plan);

        // Assert
        rows.Select(row => row.Label).ShouldBe(["cte0", "cte1", "root", "inc0", "sort", "page"]);
    }

    [Fact]
    public void GivenANonTrivialPlan_WhenDescribed_ThenTheRootRowBodyExcludesItsOwnLabel()
    {
        // Arrange
        var plan = NonTrivialPlan();

        // Act
        var root = PlanExplainer.Describe(plan).Single(row => row.Label == "root");

        // Assert
        root.Body.ShouldBe("Intersect(cte0, cte1) WHERE ResourceId = @p2");
    }

    [Fact]
    public void GivenACountOnlyPlan_WhenDescribed_ThenCountOnlyIsSplitIntoLabelAndBodyLikeEveryOtherRow()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), CountOnly: true);

        // Act
        var rows = PlanExplainer.Describe(plan);

        // Assert
        rows[^1].ShouldBe(new PlanExplainRow("countOnly", "true"));
        rows.Select(row => $"{row.Label} = {row.Body}").ShouldBe(plan.Explain().Split('\n'));
    }

    [Fact]
    public async Task GivenACompiledTrace_WhenReadingItsPlanRows_ThenTheyCarryTheSameContentAsItsExplainString()
    {
        // Arrange
        var trace = await SearchTraceFixtures.TracePatientActiveWithIncludeAsync();

        // Act
        var plan = trace.Plan.ShouldNotBeNull();

        // Assert
        plan.Rows.Select(row => $"{row.Label} = {row.Body}").ShouldBe(plan.Explain.Split('\n'));
        plan.Rows.ShouldContain(row => row.Label == "root");
        plan.Rows.ShouldContain(row => row.Label == "inc0");
    }

    /// <summary>Two parameter CTEs behind an intersect, an outer predicate, an include stage, a sort and a
    /// page boundary — every label the vocabulary defines except countOnly, and enough bound parameters that
    /// a traversal reordered by the refactor would renumber @pN visibly.</summary>
    private static QueryPlan NonTrivialPlan()
    {
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");

        return new QueryPlan(
            [
                new CteDefinition.ParamSource(
                    stringTable, 103, 202,
                    new Predicate.Equal(new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"))),
                new CteDefinition.ParamSource(
                    tokenTable, 103, 44,
                    new Predicate.Equal(new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"))),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            Match: new CteRef(2),
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123")),
            Includes: [new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000)],
            Sort: new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued),
            Page: new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L)));
    }
}
