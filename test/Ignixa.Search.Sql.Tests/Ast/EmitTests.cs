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
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
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
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            "),\n" +
            "cte1 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
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

    [Fact]
    public void GivenALikePredicateWithCollation_WhenEmitted_ThenCollateAppliesToTheColumnNotTheEscapeClause()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Like(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"), LikeMatch.StartsWith, "Latin1_General_100_CI_AI");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 202, predicate)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert -- COLLATE must bind to the column reference, immediately before LIKE, not to the
        // ESCAPE clause's literal (a postfix COLLATE there would be a syntactic no-op on the match).
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE SearchParamId = 202 AND Text COLLATE Latin1_General_100_CI_AI LIKE @p0 ESCAPE '\\'\n" +
            ")\n" +
            "SELECT T1, Sid1 FROM cte0");
    }

    [Fact]
    public void GivenACompoundAndOfTwoComparisons_WhenEmitted_ThenProducesBothConditionsJoinedByAnd()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("NumberSearchParam");
        var predicate = new Predicate.And(
            new Predicate.LessThanOrEqual(new SqlColumnRef(table.TableName, "LowValue"), new SqlParameterRef(5m)),
            new Predicate.GreaterThanOrEqual(new SqlColumnRef(table.TableName, "HighValue"), new SqlParameterRef(5m)));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 99, predicate)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("LowValue <= @p0 AND HighValue >= @p1");
        emitted.Parameters.Select(p => p.Value).ShouldBe([5m, 5m]);
    }

    [Fact]
    public void GivenAnOrOfTwoComparisons_WhenEmitted_ThenProducesBothConditionsJoinedByOrInParens()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("NumberSearchParam");
        var predicate = new Predicate.Or(
            new Predicate.LessThan(new SqlColumnRef(table.TableName, "HighValue"), new SqlParameterRef(5m)),
            new Predicate.GreaterThan(new SqlColumnRef(table.TableName, "LowValue"), new SqlParameterRef(5m)));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 99, predicate)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("(HighValue < @p0 OR LowValue > @p1)");
    }

    [Fact]
    public void GivenAResourceSourceCte_WhenEmitted_ThenSelectsFromDboResourceFilteredByType()
    {
        // Arrange
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("FROM dbo.Resource");
        emitted.Sql.ShouldContain("IsHistory = 0");
        emitted.Sql.ShouldContain("IsDeleted = 0");
        emitted.Parameters.ShouldContain(p => p.Value.Equals((short)103));
    }

    [Fact]
    public void GivenAnExceptCte_WhenEmitted_ThenUsesNotExistsAntiJoin()
    {
        // Arrange
        var plan = new QueryPlan(
            [
                new CteDefinition.ResourceSource(103),
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith"))),
                new CteDefinition.Except(new CteRef(0), new CteRef(1)),
            ],
            new CteRef(2));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("NOT EXISTS");
        emitted.Sql.ShouldNotContain("Smith");
    }
}
