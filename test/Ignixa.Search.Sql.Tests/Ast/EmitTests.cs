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
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
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
                new CteDefinition.ParamSource(stringTable, 103, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 103, 44, tokenPredicate),
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
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            "),\n" +
            "cte1 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 44 AND Code = @p1\n" +
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
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));

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
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert -- COLLATE must bind to the column reference, immediately before LIKE, not to the
        // ESCAPE clause's literal (a postfix COLLATE there would be a syntactic no-op on the match).
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text COLLATE Latin1_General_100_CI_AI LIKE @p0 ESCAPE '\\'\n" +
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
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 99, predicate)], new CteRef(0));

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
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 99, predicate)], new CteRef(0));

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
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 103, 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith"))),
                new CteDefinition.Except(new CteRef(0), new CteRef(1)),
            ],
            new CteRef(2));

        // Act
        var emitted = Emit.Run(plan);

        // Assert -- correlates on BOTH T1 and Sid1, not Sid1 alone (matters once cross-resource-type
        // queries exist; correct by construction here, not by accident of this test's single-type scope)
        emitted.Sql.ShouldContain("WHERE NOT EXISTS (\n" +
            "        SELECT 1 FROM cte1\n" +
            "        WHERE cte1.T1 = cte0.T1 AND cte1.Sid1 = cte0.Sid1)");
        emitted.Sql.ShouldNotContain("Smith");
    }

    [Fact]
    public void GivenAnOuterPredicate_WhenEmitted_ThenJoinsToDboResourceAndAppliesTheWhereClause()
    {
        // Arrange
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 103, 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith")))],
            new CteRef(0),
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123")));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        emitted.Sql.ShouldContain("ResourceId =");
        emitted.Sql.ShouldNotContain("123");
        emitted.Parameters.ShouldContain(p => p.Value.Equals("123"));
    }

    [Fact]
    public void GivenNoOuterPredicate_WhenEmitted_ThenNoJoinToDboResourceAppears()
    {
        // Arrange
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 103, 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith")))],
            new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("dbo.Resource");
    }

    [Fact]
    public void GivenAForwardChainJoin_WhenEmitted_ThenTranslatesTheOutputSideThroughResource()
    {
        // Arrange -- cte0 is some pre-existing target-side match; ChainJoin wraps it as InnerMatch
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), ResourceTypeId: 105, SearchParamId: 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Acme"))),
                new CteDefinition.ChainJoin(new CteRef(0), ReferenceSearchParamId: 55, InnerResourceTypeId: 105, OutputResourceTypeIds: [103], ChainDirection.Forward),
            ],
            new CteRef(1));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("SELECT DISTINCT rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r");
        emitted.Sql.ShouldContain("ON r.ResourceTypeId = rsp.ReferenceResourceTypeId");
        emitted.Sql.ShouldContain("AND r.ResourceId = rsp.ReferenceResourceId");
        emitted.Sql.ShouldContain("AND r.IsHistory = 0 AND r.IsDeleted = 0");
        emitted.Sql.ShouldContain("INNER JOIN cte0 m");
        emitted.Sql.ShouldContain("ON m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId");
        emitted.Sql.ShouldContain("WHERE rsp.SearchParamId = 55");
        emitted.Sql.ShouldContain("AND rsp.ReferenceResourceTypeId = 105");
        emitted.Sql.ShouldContain("AND rsp.ResourceTypeId = 103");
        emitted.Sql.ShouldContain("AND rsp.BaseUri IS NULL");
    }

    [Fact]
    public void GivenAReverseChainJoinWithPluralOutputTypes_WhenEmitted_ThenOrsTheOutputTypeFilter()
    {
        // Arrange -- cte0 is the referencing-side match; output can be more than one type
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("TokenSearchParam"), ResourceTypeId: 106, SearchParamId: 88, new Predicate.Equal(new SqlColumnRef("TokenSearchParam", "Code"), new SqlParameterRef("1234-5"))),
                new CteDefinition.ChainJoin(new CteRef(0), ReferenceSearchParamId: 77, InnerResourceTypeId: 106, OutputResourceTypeIds: [103, 108], ChainDirection.Reverse),
            ],
            new CteRef(1));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        emitted.Sql.ShouldContain("INNER JOIN cte0 m");
        emitted.Sql.ShouldContain("ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r");
        emitted.Sql.ShouldContain("ON r.ResourceTypeId = rsp.ReferenceResourceTypeId");
        emitted.Sql.ShouldContain("WHERE rsp.SearchParamId = 77");
        emitted.Sql.ShouldContain("AND rsp.ResourceTypeId = 106");
        emitted.Sql.ShouldContain("AND (rsp.ReferenceResourceTypeId = 103 OR rsp.ReferenceResourceTypeId = 108)");
        emitted.Sql.ShouldContain("AND rsp.BaseUri IS NULL");
    }

    [Fact]
    public void GivenAForwardIncludeStageSeededFromMatch_WhenEmitted_ThenProducesTheCteMatchPageShapeWithTheRAsideProjection()
    {
        // Arrange -- Patient?_include=Patient:organization, matching ChainJoin.Reverse's shape per
        // design doc §1.2: forward include's known side is rsp (already-matched Patient rows).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Forward,
            ReferenceSearchParamId: 55,
            SeedTypeIds: [103],
            OutputTypeIds: [105],
            SeedStages: [],
            SeedFromMatch: true,
            Iterate: false,
            Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Includes: [stage]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            "),\n" +
            "cteMatchPage AS (\n" +
            "    SELECT TOP (50) m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "),\n" +
            "inc0 AS (\n" +
            "    SELECT DISTINCT TOP (1001) r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam rsp\n" +
            "    INNER JOIN dbo.Resource r\n" +
            "        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "    WHERE rsp.SearchParamId = 55\n" +
            "      AND rsp.ResourceTypeId = 103\n" +
            "      AND r.ResourceTypeId = 105\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )\n" +
            "),\n" +
            "inc0lim AS (\n" +
            "    SELECT TOP (1000) T1, Sid1,\n" +
            "           CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS IsPartial\n" +
            "    FROM inc0\n" +
            ")\n" +
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC");
        emitted.Parameters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAReverseIncludeStage_WhenEmitted_ThenTheKnownSideIsTranslatedThroughDboResourceAndTheOutputSideIsSelectedDirectlyFromRsp()
    {
        // Arrange -- Patient?_revinclude=Observation:subject, matching ChainJoin.Forward's shape.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Reverse,
            ReferenceSearchParamId: 77,
            SeedTypeIds: [103],
            OutputTypeIds: [104],
            SeedStages: [],
            SeedFromMatch: true,
            Iterate: false,
            Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [stage]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            "),\n" +
            "cteMatchPage AS (\n" +
            "    SELECT m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "),\n" +
            "inc0 AS (\n" +
            "    SELECT DISTINCT TOP (1001) rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam rsp\n" +
            "    INNER JOIN dbo.Resource r\n" +
            "        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "    WHERE rsp.SearchParamId = 77\n" +
            "      AND r.ResourceTypeId = 103\n" +
            "      AND rsp.ResourceTypeId = 104\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId\n" +
            "    )\n" +
            "),\n" +
            "inc0lim AS (\n" +
            "    SELECT TOP (1000) T1, Sid1,\n" +
            "           CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS IsPartial\n" +
            "    FROM inc0\n" +
            ")\n" +
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC");
    }

    [Fact]
    public void GivenAWildcardIncludeStage_WhenEmitted_ThenNoSearchParamIdFilterIsEmitted()
    {
        // Arrange -- Patient?_include=Patient:* -- ReferenceSearchParamId is null.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Forward,
            ReferenceSearchParamId: null,
            SeedTypeIds: null,
            OutputTypeIds: null,
            SeedStages: [],
            SeedFromMatch: true,
            Iterate: false,
            Limit: 500);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [stage]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert -- no "rsp.SearchParamId = ", no type filters, straight to BaseUri + EXISTS
        emitted.Sql.ShouldContain(
            "    WHERE rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )");
        emitted.Sql.ShouldNotContain("rsp.SearchParamId = ");
    }

    [Fact]
    public void GivenAnIterateStageSeededFromAPredecessorInclude_WhenEmitted_ThenTheExistsClauseUnionsBothBranches()
    {
        // Arrange -- inc1 seeds from BOTH cteMatchPage and inc0lim.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage0 = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var stage1 = new IncludeStage(IncludeDirection.Forward, 88, [105], [105], SeedStages: [0], SeedFromMatch: true, Iterate: true, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [stage0, stage1]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "    WHERE rsp.SearchParamId = 88\n" +
            "      AND rsp.ResourceTypeId = 105\n" +
            "      AND r.ResourceTypeId = 105\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "        UNION ALL\n" +
            "        SELECT 1 FROM inc0lim m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )");
    }

    [Fact]
    public void GivenAPlanWithNoIncludes_WhenEmitted_ThenTheSqlIsByteIdenticalToThePreIncludeShape()
    {
        // Arrange -- this is the zero-diff regression proof: identical to
        // GivenASingleParamSourcePlan_WhenEmitted_ThenProducesAParameterizedSelect's arrangement, above.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            ")\n" +
            "SELECT TOP (10) T1, Sid1 FROM cte0");
    }
}
