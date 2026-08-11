using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Tests.Builders;

public class SqlTextRangeTests
{
    private static QueryPlan LeafPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new MatchPageSpec(new CteRef(0), Top: 10));
    }

    private static QueryPlan IncludesPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return IncludePlanFactory.Create(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new MatchPageSpec(new CteRef(0), Sort: sort, OffsetPage: new OffsetSpec(0, 10, ProbeExtraRow: true)),
            [includeStage]);
    }

    [Fact]
    public void GivenInvalidBounds_WhenConstructed_ThenItThrows()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SqlTextRange("cte0", SqlRangeKind.Cte, -1, 5));
        Should.Throw<ArgumentOutOfRangeException>(() => new SqlTextRange("cte0", SqlRangeKind.Cte, 0, -1));
        Should.Throw<ArgumentException>(() => new SqlTextRange(string.Empty, SqlRangeKind.Cte, 0, 5));
    }

    [Fact]
    public void GivenTracingEnabled_WhenEmitted_ThenEachRangeExtractsTheSectionItClaims()
    {
        var emitted = SqlBuilder.Run(LeafPlan(), new EmitOptions(IncludeTextRanges: true));

        emitted.TextRanges.ShouldNotBeNull();
        foreach (var range in emitted.TextRanges!)
        {
            var text = emitted.Sql.Substring(range.Start, range.Length);
            text.ShouldNotBeNullOrWhiteSpace();
        }

        var cte0 = emitted.TextRanges!.First(r => r.Label == "cte0");
        emitted.Sql.Substring(cte0.Start, cte0.Length).ShouldContain("StringSearchParam");
    }

    [Fact]
    public void GivenTracingDisabled_WhenEmitted_ThenSqlAndParametersAreByteIdentical()
    {
        var traced = SqlBuilder.Run(LeafPlan(), new EmitOptions(IncludeTextRanges: true));
        var plain = SqlBuilder.Run(LeafPlan());

        plain.Sql.ShouldBe(traced.Sql);
        plain.Parameters.Select(p => p.Name).ShouldBe(traced.Parameters.Select(p => p.Name));
        plain.TextRanges.ShouldBeNull();
    }

    [Fact]
    public void GivenAnIncludesPlan_WhenEmitted_ThenTailBlocksAreLabelledNotAutoNumbered()
    {
        var plan = IncludesPlan();

        var emitted = SqlBuilder.Run(plan, new EmitOptions(IncludeTextRanges: true));

        var labels = emitted.TextRanges!.Select(r => r.Label).ToList();
        labels.ShouldContain("cteMatchPage");
        labels.ShouldContain("inc0");
        labels.ShouldContain("inc0lim");

        var hasMisnumberedCteLabel = labels.Any(label =>
            label.StartsWith("cte", StringComparison.Ordinal)
            && label != "cteMatchPage"
            && int.TryParse(label.AsSpan(3), out var i)
            && i >= plan.Ctes.Count);
        hasMisnumberedCteLabel.ShouldBeFalse();
    }

    [Fact]
    public void GivenAnOverFetchingIncludesPlan_WhenTracingSql_ThenBothWrapperCtesHaveGraphBackedRanges()
    {
        var plan = IncludesPlan();
        var emitted = SqlBuilder.Run(plan, new EmitOptions(IncludeTextRanges: true));

        emitted.TextRanges!.ShouldContain(r => r.Label == SqlLabels.MatchPage && r.Kind == SqlRangeKind.MatchPage);
        emitted.TextRanges!.ShouldContain(r => r.Label == SqlLabels.MatchSeed && r.Kind == SqlRangeKind.MatchSeed);
        plan.Ctes.ShouldContain(c => c is CteDefinition.MatchPage);
        plan.Ctes.ShouldContain(c => c is CteDefinition.MatchSeed);
    }

    [Fact]
    public void GivenAnOffsetProbedIncludesPlan_WhenTracingSql_ThenWrapperRangesFollowTheSourceCteOrdinal()
    {
        var plan = IncludesPlan();
        var ranges = SqlBuilder.Run(plan, new EmitOptions(IncludeTextRanges: true)).TextRanges!;

        var source = ranges.Single(range => range.Label == SqlLabels.CteLabel(0));
        var matchPage = ranges.Single(range => range.Label == SqlLabels.MatchPage);
        var matchSeed = ranges.Single(range => range.Label == SqlLabels.MatchSeed);

        source.Kind.ShouldBe(SqlRangeKind.Cte);
        matchPage.Kind.ShouldBe(SqlRangeKind.MatchPage);
        matchSeed.Kind.ShouldBe(SqlRangeKind.MatchSeed);
        source.Start.ShouldBeLessThan(matchPage.Start);
        matchPage.Start.ShouldBeLessThan(matchSeed.Start);
    }

    [Fact]
    public void GivenAnIncludesPlanWithMatchFilters_WhenTracingSql_ThenTheMatchPageWhereAndSeekRangesArePreserved()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var outerPredicate = new Predicate.Equal(
            new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("patient-1"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ParamSource(table, 103, 202)],
            new MatchPageSpec(new CteRef(0), OuterPredicate: outerPredicate, Sort: sort, Page: page),
            [includeStage]);

        var ranges = SqlBuilder.Run(plan, new EmitOptions(IncludeTextRanges: true)).TextRanges!;

        ranges.ShouldContain(r => r.Label == SqlLabels.Where && r.Kind == SqlRangeKind.Where);
        ranges.ShouldContain(r => r.Label == SqlLabels.Seek && r.Kind == SqlRangeKind.Seek);
    }

    [Fact]
    public void GivenInvalidKind_WhenConstructed_ThenItThrows()
        => Should.Throw<ArgumentException>(() => new SqlTextRange("cte0", string.Empty, 0, 5));

    [Fact]
    public void GivenAnIncludesPlan_WhenEmitted_ThenTheOuterAssemblyIsCovered()
    {
        // Arrange -- the final UNION ALL belongs to no plan row, so before it was sectioned there was a
        // stretch of SQL a consumer could not address at all.
        var emitted = SqlBuilder.Run(IncludesPlan(), new EmitOptions(IncludeTextRanges: true));

        // Act
        emitted.TextRanges!.ShouldContain(r => r.Kind == SqlRangeKind.Assembly);
        var assembly = emitted.TextRanges!.First(r => r.Kind == SqlRangeKind.Assembly);

        // Assert
        emitted.Sql.Substring(assembly.Start, assembly.Length).ShouldContain("UNION ALL");
    }

    [Fact]
    public void GivenAnIncludesPlan_WhenEmitted_ThenEveryRangeCarriesAKindAndAUniqueLabel()
    {
        // Arrange & Act
        var ranges = SqlBuilder.Run(IncludesPlan(), new EmitOptions(IncludeTextRanges: true)).TextRanges!;

        // Assert -- a consumer addresses a range by label and styles it by kind; neither may be absent,
        // and a duplicate label would make the first unreachable.
        ranges.ShouldAllBe(r => !string.IsNullOrEmpty(r.Kind));
        ranges.Select(r => r.Label).ShouldBeUnique();
    }

    [Fact]
    public void GivenAnIncludeStage_WhenEmitted_ThenItsTwoRangesAreDistinguishedByKindNotBySuffix()
    {
        // Arrange & Act
        var ranges = SqlBuilder.Run(IncludesPlan(), new EmitOptions(IncludeTextRanges: true)).TextRanges!;

        // Assert -- the companion range is identifiable without stripping "lim" off the label.
        ranges.ShouldContain(r => r.Label == SqlLabels.IncludeLabel(0) && r.Kind == SqlRangeKind.Include);
        ranges.ShouldContain(r => r.Label == SqlLabels.IncludeLimitLabel(0) && r.Kind == SqlRangeKind.IncludeLimit);
    }
}
