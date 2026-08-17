using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Builders;

/// <summary>
/// ChainJoin and ReferencedTypeExpansion are the most intricate bodies CteEmitter produces -- two multi-line
/// joins, a visibility ladder spliced into one of them, and a stacked WHERE -- and they are the only emitters
/// whose whitespace the SelectBlock migration restructured non-trivially, moving the visibility line's newline
/// from trailing to leading. Nothing pinned their emitted text, so a mutation to either was invisible.
/// Both visibility branches are covered because the empty-ladder case is where a repositioned newline dangles.
/// </summary>
public class ChainAndExpansionSqlTests
{
    [Fact]
    public void GivenAForwardChainUnderCurrentVisibility_WhenEmitted_ThenItsCteBodyMatchesTheGolden()
    {
        var sql = Emit(new CteDefinition.ChainJoin(new CteRef(0), 55, 105, [103], ChainDirection.Forward), ResourceVisibility.Current);

        sql.ShouldContain(
            "cte1 AS (\n" +
            "    SELECT DISTINCT rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam rsp\n" +
            "    INNER JOIN dbo.Resource r\n" +
            "        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "    INNER JOIN cte0 m\n" +
            "        ON m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId\n" +
            "    WHERE rsp.SearchParamId = 55\n" +
            "      AND rsp.ReferenceResourceTypeId = 105\n" +
            "      AND rsp.ResourceTypeId = 103\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            ")");
    }

    [Fact]
    public void GivenAForwardChainUnderUnconstrainedVisibility_WhenEmitted_ThenTheJoinLadderHasNoDanglingLine()
    {
        var sql = Emit(new CteDefinition.ChainJoin(new CteRef(0), 55, 105, [103], ChainDirection.Forward), new ResourceVisibility(null, null));

        sql.ShouldContain(
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "    INNER JOIN cte0 m\n");
        sql.ShouldNotContain("\n\n");
    }

    [Fact]
    public void GivenAReverseChainUnderCurrentVisibility_WhenEmitted_ThenItsCteBodyMatchesTheGolden()
    {
        var sql = Emit(new CteDefinition.ChainJoin(new CteRef(0), 77, 106, [103], ChainDirection.Reverse), ResourceVisibility.Current);

        sql.ShouldContain(
            "cte1 AS (\n" +
            "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam rsp\n" +
            "    INNER JOIN cte0 m\n" +
            "        ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    INNER JOIN dbo.Resource r\n" +
            "        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "    WHERE rsp.SearchParamId = 77\n" +
            "      AND rsp.ResourceTypeId = 106\n" +
            "      AND rsp.ReferenceResourceTypeId = 103\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            ")");
    }

    [Fact]
    public void GivenAReferencedTypeExpansionUnderCurrentVisibility_WhenEmitted_ThenItsCteBodyMatchesTheGolden()
    {
        var sql = Emit(new CteDefinition.ReferencedTypeExpansion(new CteRef(0), [201, 202]), ResourceVisibility.Current);

        sql.ShouldContain(
            "cte1 AS (\n" +
            "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam rsp\n" +
            "    INNER JOIN cte0 m\n" +
            "        ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    INNER JOIN dbo.Resource r\n" +
            "        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "    WHERE (rsp.ReferenceResourceTypeId = 201 OR rsp.ReferenceResourceTypeId = 202)\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            ")");
    }

    [Fact]
    public void GivenAReferencedTypeExpansionUnderUnconstrainedVisibility_WhenEmitted_ThenTheJoinLadderHasNoDanglingLine()
    {
        var sql = Emit(new CteDefinition.ReferencedTypeExpansion(new CteRef(0), [201]), new ResourceVisibility(null, null));

        sql.ShouldContain(
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "    WHERE rsp.ReferenceResourceTypeId = 201\n");
        sql.ShouldNotContain("\n\n");
    }

    private static string Emit(CteDefinition cte, ResourceVisibility visibility)
        => SqlBuilder.Run(new QueryPlan(
            [new CteDefinition.ResourceSource(103), cte],
            new MatchPageSpec(new CteRef(1)),
            Visibility: visibility)).Sql;
}
