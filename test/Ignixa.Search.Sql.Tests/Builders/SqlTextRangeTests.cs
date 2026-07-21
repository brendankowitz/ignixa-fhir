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
        return new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10);
    }

    private static QueryPlan IncludesPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        return new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Page: page,
            Includes: [includeStage]);
    }

    [Fact]
    public void GivenInvalidBounds_WhenConstructed_ThenItThrows()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SqlTextRange("cte0", -1, 5));
        Should.Throw<ArgumentOutOfRangeException>(() => new SqlTextRange("cte0", 0, -1));
        Should.Throw<ArgumentException>(() => new SqlTextRange(string.Empty, 0, 5));
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
}
