using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class QueryPlanTests
{
    [Fact]
    public void GivenAParamSourceAndAnIntersectReferencingIt_WhenConstructed_ThenTheGraphIsWellFormed()
    {
        // Arrange
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var stringPredicate = new Predicate.Equal(
            new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var tokenPredicate = new Predicate.Equal(new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"));

        // Act
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 103, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 103, 44, tokenPredicate),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            Match: new CteRef(2),
            Top: 10);

        // Assert
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
        var intersect = plan.Ctes[2].ShouldBeOfType<CteDefinition.Intersect>();
        intersect.Left.ShouldBe(new CteRef(0));
        intersect.Right.ShouldBe(new CteRef(1));
        plan.Match.ShouldBe(new CteRef(2));
        plan.Top.ShouldBe(10);
    }

    [Fact]
    public void GivenAUnionOfTwoCteRefs_WhenConstructed_ThenPartsPreserveOrder()
    {
        // Act
        var union = new CteDefinition.Union([new CteRef(0), new CteRef(1)]);

        // Assert
        union.Parts.ShouldBe([new CteRef(0), new CteRef(1)]);
    }
}
