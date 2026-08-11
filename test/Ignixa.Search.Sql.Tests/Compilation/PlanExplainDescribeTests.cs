using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

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
            "matchPage = MatchPageCte(top=none, sortJoins=true, resourceJoin=true)\n" +
            "sort = SortSpec([String:202 ASC], Valued)\n" +
            "page = PageSpec(boundary=[@p3], type=none, sid=@p4)\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)");
    }

    [Fact]
    public void GivenANonTrivialPlan_WhenDescribed_ThenTheLabelsNameEveryStageInTraversalOrder()
    {
        // Arrange
        var plan = NonTrivialPlan();

        // Act
        var rows = PlanExplainer.Describe(plan);

        // Assert
        rows.Select(row => row.Label).ShouldBe(["cte0", "cte1", "root", "matchPage", "sort", "page", "inc0"]);
    }

    [Fact]
    public void GivenANonTrivialPlan_WhenDescribed_ThenOnlyTheMatchRowsDisplayAndCanonicalLabelsDiffer()
    {
        // Arrange
        var plan = NonTrivialPlan();

        // Act
        var rows = PlanExplainer.Describe(plan);

        // Assert -- "root" is cosmetic; cte2 is what the SQL and CteProvenance actually use.
        rows.Select(row => row.CanonicalLabel).ShouldBe(["cte0", "cte1", "cte2", "matchPage", "sort", "page", "inc0"]);
        rows.Count(row => row.Label != row.CanonicalLabel).ShouldBe(1);
    }

    [Fact]
    public void GivenANonTrivialPlan_WhenDescribed_ThenEachRowCarriesItsNodeKind()
    {
        // Arrange
        var plan = NonTrivialPlan();

        // Act
        var rows = PlanExplainer.Describe(plan);

        // Assert -- the kind comes off the plan node, so a consumer never prefix-matches the body text.
        rows.Select(row => row.Kind).ShouldBe([
            PlanRowKind.ParamSource,
            PlanRowKind.ParamSource,
            PlanRowKind.Intersect,
            PlanRowKind.MatchPageCte,
            PlanRowKind.SortSpec,
            PlanRowKind.PageSpec,
            PlanRowKind.IncludeStage,
        ]);
    }

    [Fact]
    public void GivenAStructuralCte_WhenDescribed_ThenItNamesTheCtesItComposesAsData()
    {
        // Arrange
        var plan = NonTrivialPlan();

        // Act
        var rows = PlanExplainer.Describe(plan);

        // Assert -- these are the same indices the body renders, kept as data so no consumer has to
        // regex cte(\d+) out of generated prose to rebuild the tree.
        var root = rows.First(r => r.Label == "root");
        root.ReferencedCteIndexes.ShouldBe([0, 1]);
        rows.Where(r => r.Kind == PlanRowKind.ParamSource).ShouldAllBe(r => r.ReferencedCteIndexes.Count == 0);
    }

    [Fact]
    public void GivenAnExceptCte_WhenDescribed_ThenLeftAndRightKeepTheirOrder()
    {
        // Arrange -- Except(cte0, cte1) is not Except(cte1, cte0). A transposition here would be invisible
        // to ContributingOrdinals, which unions the two, so this is the only place it can be caught.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(table, 103, 202, predicate),
                new CteDefinition.ParamSource(table, 103, 44, predicate),
                new CteDefinition.Except(new CteRef(0), new CteRef(1)),
            ],
            Match: new CteRef(2));

        // Act
        var row = PlanExplainer.Describe(plan).Single(r => r.Kind == PlanRowKind.Except);

        // Assert
        row.ReferencedCteIndexes.ShouldBe([0, 1]);
    }

    [Fact]
    public void GivenAUnionCte_WhenDescribed_ThenEveryPartIsNamedInOrder()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(table, 103, 202, predicate),
                new CteDefinition.ParamSource(table, 103, 44, predicate),
                new CteDefinition.ParamSource(table, 103, 55, predicate),
                new CteDefinition.Union([new CteRef(0), new CteRef(1), new CteRef(2)]),
            ],
            Match: new CteRef(3));

        // Act
        var row = PlanExplainer.Describe(plan).Single(r => r.Kind == PlanRowKind.Union);

        // Assert -- N-ary, so an implementation that only read two parts would truncate silently.
        row.ReferencedCteIndexes.ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void GivenTwoRowsDescribingTheSameNode_WhenCompared_ThenTheyAreEqual()
    {
        // Arrange -- a record compares a collection property by reference, so without the explicit
        // Equals these two are unequal despite being identical.
        var left = new PlanExplainRow("root", "cte2", PlanRowKind.Intersect, "Intersect(cte0, cte1)", [0, 1]);
        var right = new PlanExplainRow("root", "cte2", PlanRowKind.Intersect, "Intersect(cte0, cte1)", [0, 1]);

        // Assert
        left.ShouldBe(right);
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void GivenAnEmptyLabelOrKind_WhenConstructed_ThenItThrows()
    {
        Should.Throw<ArgumentException>(() => new PlanExplainRow(string.Empty, "cte0", PlanRowKind.ParamSource, "b", []));
        Should.Throw<ArgumentException>(() => new PlanExplainRow("cte0", string.Empty, PlanRowKind.ParamSource, "b", []));
        Should.Throw<ArgumentException>(() => new PlanExplainRow("cte0", "cte0", string.Empty, "b", []));
        Should.Throw<ArgumentOutOfRangeException>(() => new PlanExplainRow("cte0", "cte0", PlanRowKind.Intersect, "b", [-1]));
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
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Shape: new ResultShape.Count.AllMatches());

        // Act
        var rows = PlanExplainer.Describe(plan);

        // Assert
        rows[^1].ShouldBe(new PlanExplainRow("countOnly", "countOnly", PlanRowKind.CountOnly, "true", []));
    }

    [Fact]
    public async Task GivenACompiledTrace_WhenReadingItsPlanRows_ThenEveryStageOfThatPlanIsAddressableByLabel()
    {
        // Arrange
        var result = await CompilationFixtures.TracePatientActiveWithIncludeAsync();

        // Act
        var plan = result.Plan!.Diagnostics!.PlanTrace.ShouldNotBeNull();

        // Assert
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
            Page: new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L)));
    }
}
