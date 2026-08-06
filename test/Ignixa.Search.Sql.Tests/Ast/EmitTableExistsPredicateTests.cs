using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class EmitTableExistsPredicateTests
{
    [Fact]
    public void GivenATableExistsPredicateWithNoPredicate_WhenEmitted_ThenSelectsWithNoWhereClause()
    {
        // Arrange -- "does this resource have any date-typed search-index row at all"
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var plan = new QueryPlan([new CteDefinition.TableExistsPredicate(table)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- no Top specified on the plan, so Emit's own default (no TOP clause) applies (see
        // EmitTests.cs's CompartmentSource-only case for the same no-Top rendering).
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.DateTimeSearchParam\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenATableExistsPredicateWithADateRangePredicate_WhenEmitted_ThenFiltersByIt()
    {
        // Arrange -- "does this resource have a date-typed row matching this range"
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var predicate = new Predicate.GreaterThanOrEqual(new SqlColumnRef(table.TableName, "StartDateTime"), new SqlParameterRef("2020-01-01T00:00:00.0000000"));
        var plan = new QueryPlan([new CteDefinition.TableExistsPredicate(table, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("WHERE StartDateTime >= @p0");
    }
}
