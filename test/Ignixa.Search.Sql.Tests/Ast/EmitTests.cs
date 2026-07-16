using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Tests.Ast;

public class EmitTests
{
    [Fact]
    public void GivenASingleParamSourcePlan_WhenEmitted_ThenProducesAParameterizedSelect()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 202, predicate)], new CteRef(0), Top: 10);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            ")\n" +
            "SELECT TOP (10) T1, Sid1 FROM cte0");
        emitted.Parameters.Count.ShouldBe(1);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", "Smith"));
    }

    [Fact]
    public void GivenAnIntersectOfTwoParamSources_WhenEmitted_ThenJoinsThemOnResourceIdentity()
    {
        // Arrange
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var stringPredicate = new Predicate.Equal(
            new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var tokenPredicate = new Predicate.Equal(new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"));
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 44, tokenPredicate),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            new CteRef(2));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            "),\n" +
            "cte1 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE SearchParamId = 44 AND Code = @p1\n" +
            "),\n" +
            "cte2 AS (\n" +
            "    SELECT cte0.T1, cte0.Sid1\n" +
            "    FROM cte0\n" +
            "    INNER JOIN cte1 ON cte0.T1 = cte1.T1 AND cte0.Sid1 = cte1.Sid1\n" +
            ")\n" +
            "SELECT T1, Sid1 FROM cte2");
        emitted.Parameters.Select(p => p.Name).ShouldBe(["@p0", "@p1"]);
    }

    [Fact]
    public void GivenAnyPlanWithAUserValue_WhenEmitted_ThenTheValueNeverAppearsInSqlTextOnlyInParameters()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Like(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Zorbaxil%"), LikeMatch.Contains, "Latin1_General_100_CI_AI");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 202, predicate)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        // "Zorbaxil" is chosen because it cannot collide with any legitimate SQL token this Emit call
        // produces (table/column names, SearchParamId, or the "Latin1_General_100_CI_AI" collation
        // literal, which legitimately contains "100" and would make that value a false-positive probe).
        emitted.Sql.ShouldNotContain("Zorbaxil");
        emitted.Parameters.ShouldContain(p => p.Value.Equals("%Zorbaxil\\%%"));
    }
}
