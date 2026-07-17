using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class PlanExplainerTests
{
    [Fact]
    public void GivenASingleParamSourcePlan_WhenExplained_ThenPrintsTheColumnComparisonAsRoot()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"),
            new SqlParameterRef("Smith"),
            "Latin1_General_100_CS_AS");
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            Match: new CteRef(0),
            Top: 10);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = StringSearchParam[103,202]  Text = @p0 collate CS_AS top 10");
    }

    [Fact]
    public void GivenAnIntersectOfTwoParamSources_WhenExplained_ThenLeavesAreNumberedAndRootReferencesThem()
    {
        // Arrange
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var stringPredicate = new Predicate.Equal(
            new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var tokenPredicate = new Predicate.Equal(
            new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"));
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 103, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 103, 44, tokenPredicate),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            Match: new CteRef(2));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "cte0 = StringSearchParam[103,202]  Text = @p0 collate CS_AS\n" +
            "cte1 = TokenSearchParam[103,44]  Code = @p1\n" +
            "root = Intersect(cte0, cte1)");
    }

    [Fact]
    public void GivenACompoundAndOfTwoComparisons_WhenExplained_ThenPrintsBothConditions()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("NumberSearchParam");
        var predicate = new Predicate.And(
            new Predicate.LessThanOrEqual(new SqlColumnRef(table.TableName, "LowValue"), new SqlParameterRef(5m)),
            new Predicate.GreaterThanOrEqual(new SqlColumnRef(table.TableName, "HighValue"), new SqlParameterRef(5m)));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 99, predicate)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = NumberSearchParam[103,99]  LowValue <= @p0 AND HighValue >= @p1");
    }

    [Fact]
    public void GivenAnOrOfTwoComparisons_WhenExplained_ThenPrintsBothConditionsJoinedByOrWithSequentialOrdinals()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("NumberSearchParam");
        var predicate = new Predicate.Or(
            new Predicate.LessThan(new SqlColumnRef(table.TableName, "HighValue"), new SqlParameterRef(5m)),
            new Predicate.GreaterThan(new SqlColumnRef(table.TableName, "LowValue"), new SqlParameterRef(5m)));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 99, predicate)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = NumberSearchParam[103,99]  HighValue < @p0 OR LowValue > @p1");
    }

    [Fact]
    public void GivenAResourceSourceCte_WhenExplained_ThenRendersResourceTypeId()
    {
        // Arrange
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = ResourceSource[103]");
    }

    [Fact]
    public void GivenAnExceptCte_WhenExplained_ThenRendersBothOperands()
    {
        // Arrange
        var plan = new QueryPlan(
        [
            new CteDefinition.ResourceSource(103),
            new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 103, 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith"))),
            new CteDefinition.Except(new CteRef(0), new CteRef(1)),
        ],
        new CteRef(2));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "cte0 = ResourceSource[103]\n" +
            "cte1 = StringSearchParam[103,202]  Text = @p1\n" +
            "root = Except(cte0, cte1)");
    }

    [Fact]
    public void GivenAnOuterPredicate_WhenExplained_ThenAppendsWhereToTheRootLine()
    {
        // Arrange
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 103, 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith")))],
            new CteRef(0),
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123")));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = StringSearchParam[103,202]  Text = @p0 WHERE ResourceId = @p1");
    }
}
