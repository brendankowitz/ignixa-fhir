using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class EmitVisibleSinceFilterTests
{
    [Fact]
    public void GivenAVisibleSinceFilter_WhenEmitted_ThenJoinsTransactionsOnVisibleDate()
    {
        // Arrange
        var since = new SqlParameterRef("2020-01-01T00:00:00.0000000");
        var plan = new QueryPlan([new CteDefinition.VisibleSinceFilter(since)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- no Top specified on the plan, so Emit's own default (no TOP clause) applies (see
        // EmitTableExistsPredicateTests's no-predicate case for the same no-Top rendering). The default
        // visibility clause is part of the expected SQL: this emitter originally emitted no visibility
        // filter at all, which left one $everything CTE kind outside the contract every other emitter
        // honours -- a resource deleted or superseded since the cutoff would still have come back.
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.Resource r\n" +
            "    INNER JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue\n" +
            "    WHERE t.VisibleDate >= @p0 AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }
}
