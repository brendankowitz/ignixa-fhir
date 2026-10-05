using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Tests.TestSupport;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Ignixa.Search.Sql.Tests.Ast;

public class UnboundedIncludeTraversalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GivenUnboundedIncludeStages_WhenCompiled_ThenNoStageTruncatesTheTraversal(bool includesOnly)
    {
        var plan = IncludePlanFactory.Create(
            [new CteDefinition.ResourceSource(103)],
            new MatchPageSpec(new CteRef(0),
                Shape: includesOnly ? new ResultShape.IncludesPage() : ResultShape.Default),
            [
                new IncludeStage(IncludeDirection.Reverse, 55, [103], [104], [],
                    SeedFromMatch: true, Iterate: false, Limit: null),
                new IncludeStage(IncludeDirection.Forward, 88, [104], [105], [0],
                    SeedFromMatch: false, Iterate: true, Limit: null),
            ]);

        var sql = SqlBuilder.Run(plan).Sql;

        SqlGrammar.AssertValid(sql);
        SqlGrammar.AssertEveryReferencedCteIsDefined(sql);
        SqlGrammar.Count<TopRowFilter>(SqlGrammar.Parse(sql)).ShouldBe(0);
    }
}
