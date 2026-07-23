using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
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
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            ")\n" +
            "SELECT TOP (10) m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
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
        var emitted = SqlBuilder.Run(plan);

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
            "SELECT m.T1, m.Sid1 FROM cte2 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
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
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

        // Assert -- COLLATE must bind to the column reference, immediately before LIKE, not to the
        // ESCAPE clause's literal (a postfix COLLATE there would be a syntactic no-op on the match).
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text COLLATE Latin1_General_100_CI_AI LIKE @p0 ESCAPE '\\'\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
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
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("(HighValue < @p0 OR LowValue > @p1)");
    }

    [Fact]
    public void GivenAResourceSourceCte_WhenEmitted_ThenSelectsFromDboResourceFilteredByType()
    {
        // Arrange
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

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
            "    ORDER BY m.T1 ASC, m.Sid1 ASC\n" +
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
            "    ORDER BY T1 ASC, Sid1 ASC\n" +
            "),\n" +
            "inc0lim AS (\n" +
            "    SELECT TOP (1000) T1, Sid1,\n" +
            "           CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS IsPartial\n" +
            "    FROM inc0\n" +
            "    ORDER BY T1 ASC, Sid1 ASC\n" +
            ")\n" +
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC, T1 ASC, Sid1 ASC");
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
        var emitted = SqlBuilder.Run(plan);

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
            "    ORDER BY T1 ASC, Sid1 ASC\n" +
            "),\n" +
            "inc0lim AS (\n" +
            "    SELECT TOP (1000) T1, Sid1,\n" +
            "           CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS IsPartial\n" +
            "    FROM inc0\n" +
            "    ORDER BY T1 ASC, Sid1 ASC\n" +
            ")\n" +
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC, T1 ASC, Sid1 ASC");
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
        var emitted = SqlBuilder.Run(plan);

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
        var emitted = SqlBuilder.Run(plan);

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
    public void GivenAPlanWithNoIncludesAndNoSort_WhenEmitted_ThenTheSqlHasTheDefaultTypeAndSurrogateIdOrdering()
    {
        // Arrange -- identical to
        // GivenASingleParamSourcePlan_WhenEmitted_ThenProducesAParameterizedSelect's arrangement, above.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            ")\n" +
            "SELECT TOP (10) m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenACompartmentSourcePlan_WhenEmitted_ThenProducesAGroupedSelectWithTheTypeOrChainAndTheReferencePredicate()
    {
        // Arrange -- Patient/123 compartment, "subject" SearchParamId 77, spanning Observation(104)/Condition(106).
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var predicate = new Predicate.And(
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"), new SqlParameterRef((short)103)),
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceId"), new SqlParameterRef("123")));
        var plan = new QueryPlan([new CteDefinition.CompartmentSource([104, 106], 77, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam\n" +
            "    WHERE SearchParamId = 77\n" +
            "      AND (ResourceTypeId = 104 OR ResourceTypeId = 106)\n" +
            "      AND (ReferenceResourceTypeId = @p0 AND ReferenceResourceId = @p1)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(2);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", (short)103));
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "123"));
    }

    [Fact]
    public void GivenACompartmentSourceWithASingleResourceType_WhenEmitted_ThenTheTypeFilterIsABareEqualNotAnOrChain()
    {
        // Arrange -- the non-wildcard case (design §4): one grouped SearchParamId, one resource type.
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var predicate = new Predicate.And(
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"), new SqlParameterRef((short)103)),
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceId"), new SqlParameterRef("123")));
        var plan = new QueryPlan([new CteDefinition.CompartmentSource([104], 77, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("      AND ResourceTypeId = 104\n");
        emitted.Sql.ShouldNotContain("(ResourceTypeId = 104)");
    }

    [Fact]
    public void GivenASingleAscendingStringSortKeyInTheValuedPhase_WhenEmitted_ThenJoinsOnIsMinAndOrdersByTheJoinedColumn()
    {
        // Arrange -- Patient?_sort=name, first page (no boundary).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            ")\n" +
            "SELECT TOP (10) m.T1, m.Sid1, sk0.Text AS SortValue0 FROM cte0 m\n" +
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 202 AND sk0.IsMin = 1\n" +
            "ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenASortWithAPageBoundary_WhenEmitted_ThenTheSeekPredicateAppearsInTheWhereClause()
    {
        // Arrange -- Patient?_sort=name, second page.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "WHERE (sk0.Text > @p1\n" +
            "       OR (sk0.Text = @p1 AND m.T1 = @p2 AND m.Sid1 > @p3)\n" +
            "       OR (sk0.Text = @p1 AND m.T1 > @p2))\n" +
            "ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(4);
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "Adams"));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", (short)103));
        emitted.Parameters[3].ShouldBe(new EmittedSqlParameter("@p3", 5000L));
    }

    [Fact]
    public void GivenTheMissingPrimaryPhase_WhenEmitted_ThenTheJoinIsReplacedByNotExistsAndTheOrderByOmitsTheMissingKey()
    {
        // Arrange -- Patient?_sort=name, second (missing-name) phase, no secondary keys.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.MissingPrimary);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain(
            "SELECT TOP (10) m.T1, m.Sid1 FROM cte0 m\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202)\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenAMultiKeySortWithMixedDirectionsAndASecondaryKeyTie_WhenEmitted_ThenTheOrderByAndSeekPredicateUseTheIdenticalIsNullExpression()
    {
        // Arrange -- Patient?_sort=name,-birthdate, valued phase, second key uses the F1 invariant
        // (ISNULL identical in ORDER BY and seek) since it's a LEFT-JOIN tie-breaker, not the primary.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec(
            [
                new SortKey(202, SortKeyKind.String, SortOrder.Ascending),
                new SortKey(303, SortKeyKind.Date, SortOrder.Descending),
            ],
            SortPhase.Valued);
        var page = new PageSpec(
            [new SqlParameterRef("Zorro"), new SqlParameterRef("2000-01-01T00:00:00.0000000")],
            new SqlParameterRef((short)103),
            new SqlParameterRef(9000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort, Page: page);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- same ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') text in both places.
        emitted.Sql.ShouldContain(
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 202 AND sk0.IsMin = 1\n" +
            "LEFT JOIN dbo.DateTimeSearchParam sk1\n" +
            "    ON sk1.ResourceTypeId = m.T1 AND sk1.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk1.SearchParamId = 303 AND sk1.IsMax = 1");
        emitted.Sql.ShouldContain(
            "WHERE (sk0.Text > @p1\n" +
            "       OR (sk0.Text = @p1 AND ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') < @p2)\n" +
            "       OR (sk0.Text = @p1 AND ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') = @p2 AND m.T1 = @p3 AND m.Sid1 > @p4)\n" +
            "       OR (sk0.Text = @p1 AND ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') = @p2 AND m.T1 > @p3))\n" +
            "ORDER BY sk0.Text ASC, ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') DESC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenALastUpdatedSortKey_WhenEmitted_ThenNoJoinIsEmittedAndTheOrderByUsesTheSurrogateIdDirectly()
    {
        // Arrange -- Patient?_sort=-_lastUpdated.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SortOrder.Descending)], SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("JOIN dbo.");
        emitted.Sql.ShouldContain("SELECT m.T1, m.Sid1, m.Sid1 AS SortValue0 FROM cte0 m\n");
        emitted.Sql.ShouldContain("ORDER BY m.Sid1 DESC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenNoSortButAPageBoundary_WhenEmitted_ThenTheSeekPredicateIsTheBareTypeAndSurrogateIdTupleOnly()
    {
        // Arrange -- an ordinary, unsorted paginated search (design §2's "no sort" keyset case).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var page = new PageSpec([], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Page: page);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- branches.Count == 2 here (no key levels, just the two final type/sid tie-break
        // branches), so EmitSeekPredicate's multi-branch join applies: "\n       OR ", not a single
        // space -- matching every other multi-branch case in this same method, not a special case.
        // The whole 2-branch chain is itself wrapped in parens (branches.Count > 1) so it stays a
        // single AND-safe unit even though there's no sibling WHERE clause in THIS test to prove it.
        emitted.Sql.ShouldContain(
            "WHERE ((m.T1 = @p1 AND m.Sid1 > @p2)\n" +
            "       OR (m.T1 > @p1))\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenAnIncludeBearingPlanWithASortKey_WhenEmitted_ThenCteMatchPageCarriesTheSortJoinAndTheOuterUnionProjectsSortValueColumns()
    {
        // Arrange -- Patient?_sort=name&_include=Patient:organization.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Sort: sort,
            Includes: [includeStage]);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "cteMatchPage AS (\n" +
            "    SELECT TOP (50) m.T1, m.Sid1, sk0.Text AS SortValue0\n" +
            "    FROM cte0 m\n" +
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 202 AND sk0.IsMin = 1\n" +
            "    ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldContain(
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, SortValue0 FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial, NULL FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC, SortValue0 ASC, T1 ASC, Sid1 ASC");
    }

    [Fact]
    public void GivenAnIncludeBearingPlanWithNoSortAndNoTop_WhenEmitted_ThenCteMatchPageHasNoOrderByButTheOuterOrderByStillGetsATieBreak()
    {
        // Arrange -- Patient?_include=Patient:organization, no _sort, no _top -- cteMatchPage has no
        // TOP either, so it must have no ORDER BY of its own (SQL Server Msg 1033: ORDER BY is invalid
        // inside a CTE without TOP/OFFSET/FOR XML). The outer UNION ALL's ORDER BY is a plain top-level
        // SELECT and still applies unconditionally, giving the whole statement a deterministic ordering.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [includeStage]);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "cteMatchPage AS (\n" +
            "    SELECT m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "),\n");
        emitted.Sql.ShouldContain("    ORDER BY T1 ASC, Sid1 ASC\n");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, T1 ASC, Sid1 ASC");
    }

    [Fact]
    public void GivenTheMissingPrimaryPhaseWithALastUpdatedPrimaryKey_WhenEmitted_ThenThrowsInvalidOperationException()
    {
        // Arrange -- hand-constructed QueryPlan bypassing Lower.BuildSortSpec's own guard (Lower rejects
        // this combination at construction time -- see LowerTests' equivalent throw test). QueryPlan is
        // a public construction surface, so Emit defends against this shape too rather than trusting
        // every caller to route through Lower: _lastUpdated is never "missing," so EmitMissingPrimaryFilter
        // must never be asked to render a NOT EXISTS for it (its SearchParamId is null by construction).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SortOrder.Ascending)], SortPhase.MissingPrimary);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => SqlBuilder.Run(plan));
    }

    [Fact]
    public void GivenAPageBoundaryWithFewerValuesThanActiveSortKeys_WhenEmitted_ThenThrowsInvalidOperationExceptionMentioningTheMismatch()
    {
        // Arrange -- a 2-key Valued sort needs a 2-value boundary; this one only carries 1. Silently
        // pairing boundaryParams[0] against the wrong key's expression is exactly the silent-wrong-
        // pagination failure class this guard exists to prevent.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec(
            [
                new SortKey(202, SortKeyKind.String, SortOrder.Ascending),
                new SortKey(303, SortKeyKind.Date, SortOrder.Descending),
            ],
            SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Zorro")], new SqlParameterRef((short)103), new SqlParameterRef(9000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort, Page: page);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => SqlBuilder.Run(plan)).Message.ShouldContain("1 value(s)");
    }

    [Fact]
    public void GivenAMissingPrimaryPhaseBoundaryReusedFromTheValuedPhaseShape_WhenEmitted_ThenThrowsInvalidOperationExceptionMentioningTheMismatch()
    {
        // Arrange -- MissingPrimary excludes Keys[0] from ActiveKeyIndices, so its boundary should carry
        // Keys.Count - 1 values. Handing it a full Keys.Count-sized boundary (the Valued-phase shape) must
        // throw rather than silently misalign boundaryParams against the wrong keys.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.MissingPrimary);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => SqlBuilder.Run(plan)).Message.ShouldContain("active key(s)");
    }

    [Fact]
    public void GivenASortedIncludedSearchOnPageTwo_WhenEmitted_ThenTheSeekPredicateAppearsInsideCteMatchPageAlongsideTheSortJoinAndTheOuterUnionStillOrders()
    {
        // Arrange -- Patient?name=Smith&_sort=name&_include=Patient:organization, page 2. The flagship
        // production scenario the final review flagged as unproven: Sort + Page + Includes all at once.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Page: page,
            Includes: [includeStage]);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the seek predicate is inside cteMatchPage's own WHERE, alongside the IsMin join.
        emitted.Sql.ShouldContain(
            "cteMatchPage AS (\n" +
            "    SELECT TOP (10) m.T1, m.Sid1, sk0.Text AS SortValue0\n" +
            "    FROM cte0 m\n" +
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 202 AND sk0.IsMin = 1\n" +
            "    WHERE (sk0.Text > @p1\n" +
            "       OR (sk0.Text = @p1 AND m.T1 = @p2 AND m.Sid1 > @p3)\n" +
            "       OR (sk0.Text = @p1 AND m.T1 > @p2))\n" +
            "    ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldContain(
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, SortValue0 FROM cteMatchPage");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, SortValue0 ASC, T1 ASC, Sid1 ASC");
    }

    [Fact]
    public void GivenTheMissingPrimaryPhaseWithIncludes_WhenEmitted_ThenCteMatchPageUsesNotExistsCombinedWithTheIncludeMachinery()
    {
        // Arrange -- Patient?_sort=name&_include=Patient:organization, missing-name phase: proves the
        // NOT EXISTS filter (not the INNER JOIN) is what seeds the include stage's EXISTS-against-
        // cteMatchPage correlation, exactly as it does with no includes present.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.MissingPrimary);
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Includes: [includeStage]);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain(
            "cteMatchPage AS (\n" +
            "    SELECT TOP (10) m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "    WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202)\n" +
            "    ORDER BY m.T1 ASC, m.Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldContain(
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )");
        emitted.Sql.ShouldContain("SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, T1 ASC, Sid1 ASC");
    }

    [Fact]
    public void GivenAnOuterPredicateAndASortWithAPageBoundary_WhenEmitted_ThenTheSeekPredicateOrChainIsParenthesizedSoItStaysAndedWithTheOuterFilter()
    {
        // Arrange -- Patient?_lastUpdated=gt...&_sort=name, second page: the exact combination the
        // Checkpoint 1.5 review flagged as silently wrong. Before the fix, EmitSeekPredicate's
        // multi-branch OR chain was joined into whereClauses UNPARENTHESIZED, so "AND" bound tighter
        // than "OR" and the outer filter only applied to the seek predicate's FIRST branch -- the two
        // type/sid tie-break branches bypassed the outer filter entirely (page 2+ could return rows
        // that violate the filter).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Page: page,
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123")));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the outer filter is ANDed against the whole parenthesized OR chain as a single
        // unit, not just its first branch. If the seek predicate's OR chain were unparenthesized, this
        // exact "WHERE {outer} AND (...)" text would not appear -- the second/third OR branches would
        // instead sit at the top level, bypassing ResourceId = @p1 entirely.
        emitted.Sql.ShouldContain(
            "WHERE ResourceId = @p1 AND (sk0.Text > @p2\n" +
            "       OR (sk0.Text = @p2 AND m.T1 = @p3 AND m.Sid1 > @p4)\n" +
            "       OR (sk0.Text = @p2 AND m.T1 > @p3))\n" +
            "ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(5);
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "123"));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", "Adams"));
    }

    [Fact]
    public void GivenTheMissingPrimaryPhaseWithAMultiBranchPageBoundary_WhenEmitted_ThenTheNotExistsFilterAppliesToEveryBranchOfTheParenthesizedSeekPredicate()
    {
        // Arrange -- Patient?_sort=name,-birthdate, missing-name phase, second page: a two-key sort so
        // the MissingPrimary phase's seek predicate has 3 branches (one active-key level plus the two
        // type/sid tie-break branches), not just the 2-branch degenerate case -- proving NOT EXISTS
        // combines correctly with EVERY branch, not merely the first one it happens to sit beside.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec(
            [
                new SortKey(202, SortKeyKind.String, SortOrder.Ascending),
                new SortKey(303, SortKeyKind.Date, SortOrder.Descending),
            ],
            SortPhase.MissingPrimary);
        var page = new PageSpec(
            [new SqlParameterRef("2000-01-01T00:00:00.0000000")],
            new SqlParameterRef((short)103),
            new SqlParameterRef(9000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- before the fix, this "NOT EXISTS(...) AND (branch0 OR branch1 OR branch2)" text
        // would not exist: NOT EXISTS would only bind to branch0 via AND, and branch1/branch2 would sit
        // at the top level unfiltered, letting rows WITH a name value (that NOT EXISTS was meant to
        // exclude) leak into the missing-name phase's page 2+ results.
        emitted.Sql.ShouldContain(
            "WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202) " +
            "AND (ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') < @p1\n" +
            "       OR (ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') = @p1 AND m.T1 = @p2 AND m.Sid1 > @p3)\n" +
            "       OR (ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') = @p1 AND m.T1 > @p2))\n" +
            "ORDER BY ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') DESC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenAParamSourceWithNoPredicate_WhenEmitted_ThenTheWhereClauseHasNoTrailingAndClause()
    {
        // Arrange -- the shape Task 3's LowerParameterPresence will produce: "any row exists for this parameter."
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.ShouldBeEmpty();
    }

    [Fact]
    public void GivenACountOnlyPlanWithNoOuterPredicate_WhenEmitted_ThenTheSqlIsACountBigDistinctQuery()
    {
        // Arrange -- Patient?name=Smith&_total=accurate, no resource-column predicate.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), CountOnly: true);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            ")\n" +
            "SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m");
        emitted.Parameters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenACountOnlyPlanWithAnOuterPredicate_WhenEmitted_ThenTheSqlJoinsResourceAndFiltersBeforeCounting()
    {
        // Arrange -- Patient?_id=abc&_total=accurate (a resource-column OuterPredicate case).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outerPredicate = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("abc"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0),
            OuterPredicate: outerPredicate, CountOnly: true);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m\n" +
            "INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1\n" +
            "WHERE ResourceId = @p1");
    }

    [Fact]
    public void GivenACountOnlyPlanWithSortAndTopAndIncludesAllSet_WhenEmitted_ThenTheyAreAllIgnored()
    {
        // Arrange -- proves CountOnly wins unconditionally, regardless of what else is set on the plan
        // (a caller should never populate these for a count request, but Emit must not depend on that).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0),
            Top: 10, Sort: sort, Includes: [includeStage], CountOnly: true);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- no TOP, no ORDER BY, no sort join, no cteMatchPage, no UNION ALL anywhere.
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            ")\n" +
            "SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m");
    }

    [Fact]
    public void GivenAnIsNullPredicate_WhenEmitted_ThenProducesIsNullWithNoParameters()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var predicate = new Predicate.IsNull(new SqlColumnRef(table.TableName, "SystemId"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 44, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 44 AND SystemId IS NULL\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenAFalsePredicate_WhenEmitted_ThenProduces1Equals0WithNoParameters()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var predicate = new Predicate.False();
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 44, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 44 AND 1 = 0\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenAPrefixOfParameterPredicate_WhenEmitted_ThenProducesLeftLenComparison()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("UriSearchParam");
        var predicate = new Predicate.PrefixOfParameter(
            new SqlColumnRef(table.TableName, "Uri"),
            new SqlParameterRef("http://example.org/fhir/Patient/123"),
            "Latin1_General_100_BIN2");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.UriSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND LEFT(@p0, LEN(Uri)) COLLATE Latin1_General_100_BIN2 = Uri\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(1);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", "http://example.org/fhir/Patient/123"));
    }

    [Fact]
    public void GivenAPrefixOfParameterPredicateWithoutCollation_WhenEmitted_ThenProducesLeftLenComparisonWithoutCollate()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("UriSearchParam");
        var predicate = new Predicate.PrefixOfParameter(
            new SqlColumnRef(table.TableName, "Uri"),
            new SqlParameterRef("http://example.org/fhir/Patient/123"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("LEFT(@p0, LEN(Uri)) = Uri");
        emitted.Sql.ShouldNotContain("COLLATE");
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", "http://example.org/fhir/Patient/123"));
    }

    [Fact]
    public void GivenADualColumnContainsPredicate_WhenEmitted_ThenProducesFullyParenthesizedOrWithIsNullGuardAndTwoLikeParameters()
    {
        // Arrange — the Or(And(IsNull(TextOverflow), Like(Text)), Like(TextOverflow)) shape produced
        // by StringLoweringRule for :contains within the inline width.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var textColumn = new SqlColumnRef(table.TableName, "Text");
        var overflowColumn = new SqlColumnRef(table.TableName, "TextOverflow");
        var predicate = new Predicate.Or(
            new Predicate.And(
                new Predicate.IsNull(overflowColumn),
                new Predicate.Like(textColumn, new SqlParameterRef("mit"), LikeMatch.Contains, "Latin1_General_100_CI_AI")),
            new Predicate.Like(overflowColumn, new SqlParameterRef("mit"), LikeMatch.Contains, "Latin1_General_100_CI_AI"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert — exact full SQL
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND ((TextOverflow IS NULL AND Text COLLATE Latin1_General_100_CI_AI LIKE @p0 ESCAPE '\\') OR TextOverflow COLLATE Latin1_General_100_CI_AI LIKE @p1 ESCAPE '\\')\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(2);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", "%mit%"));
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "%mit%"));
    }

    [Fact]
    public void GivenADualColumnContainsWithSpecialCharacters_WhenEmitted_ThenBothParametersAreEscapedOnceAndWrappedWithPercent()
    {
        // Arrange — value containing all four LIKE metacharacters: %, _, [, \
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var textColumn = new SqlColumnRef(table.TableName, "Text");
        var overflowColumn = new SqlColumnRef(table.TableName, "TextOverflow");
        var specialValue = @"%_[\";
        var predicate = new Predicate.Or(
            new Predicate.And(
                new Predicate.IsNull(overflowColumn),
                new Predicate.Like(textColumn, new SqlParameterRef(specialValue), LikeMatch.Contains, "Latin1_General_100_CI_AI")),
            new Predicate.Like(overflowColumn, new SqlParameterRef(specialValue), LikeMatch.Contains, "Latin1_General_100_CI_AI"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert — escaped once: \ → \\, % → \%, _ → \_, [ → \[ then wrapped %...%
        var expectedPattern = @"%\%\_\[\\%";
        emitted.Parameters.Count.ShouldBe(2);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", expectedPattern));
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", expectedPattern));
    }

    [Fact]
    public void GivenANotPredicateInTheOuterWhere_WhenEmitted_ThenWrapsTheOperandInNot()
    {
        // Arrange -- Patient?_id:not=a,b -- a negated resource-column filter over a Resource scan.
        var idColumn = new SqlColumnRef("Resource", "ResourceId");
        var outer = new Predicate.Not(
            new Predicate.Or(
                new Predicate.Equal(idColumn, new SqlParameterRef("a")),
                new Predicate.Equal(idColumn, new SqlParameterRef("b"))));
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0), OuterPredicate: outer);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("WHERE NOT ((ResourceId = @p1 OR ResourceId = @p2))");
        emitted.Parameters.Select(p => p.Value).ShouldBe([(object)(short)103, "a", "b"]);
    }

    [Fact]
    public void GivenANotReferencedSourceWithSourceTypeAndPath_WhenEmitted_ThenAntiJoinsReferenceSearchParamByTargetIdentity()
    {
        // Arrange -- Patient?_not-referenced=Observation:subject. Target type 103, source type 96, ref
        // param 969. The anti-join correlates on reference-target identity: a ReferenceSearchParam row's
        // ReferenceResourceId/ReferenceResourceTypeId against the candidate Resource's own id and type.
        var plan = new QueryPlan([new CteDefinition.NotReferencedSource(103, 96, 969)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.Resource r\n" +
            "    WHERE r.ResourceTypeId = @p0 AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "      AND NOT EXISTS (\n" +
            "        SELECT 1\n" +
            "        FROM dbo.ReferenceSearchParam rsp\n" +
            "        WHERE rsp.ReferenceResourceId = r.ResourceId\n" +
            "          AND rsp.ReferenceResourceTypeId = r.ResourceTypeId\n" +
            "          AND rsp.ResourceTypeId = 96\n" +
            "          AND rsp.SearchParamId = 969)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.ShouldHaveSingleItem().ShouldBe(new EmittedSqlParameter("@p0", (short)103));
    }

    [Fact]
    public void GivenANotReferencedSourceWithSourceTypeButNoPath_WhenEmitted_ThenFiltersOnSourceTypeButNotSearchParamId()
    {
        // Arrange -- Patient?_not-referenced=Observation:* -- source type 96, no reference path. The
        // anti-join narrows to references originating from Observation, but not to any single path.
        var plan = new QueryPlan([new CteDefinition.NotReferencedSource(103, 96, null)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "      AND NOT EXISTS (\n" +
            "        SELECT 1\n" +
            "        FROM dbo.ReferenceSearchParam rsp\n" +
            "        WHERE rsp.ReferenceResourceId = r.ResourceId\n" +
            "          AND rsp.ReferenceResourceTypeId = r.ResourceTypeId\n" +
            "          AND rsp.ResourceTypeId = 96)\n");
        emitted.Sql.ShouldNotContain("rsp.SearchParamId");
    }

    [Fact]
    public void GivenANotReferencedSourceFullWildcard_WhenEmitted_ThenTheAntiJoinFiltersOnlyOnTargetIdentity()
    {
        // Arrange -- Patient?_not-referenced=*:* -- a Patient referenced by nothing at all.
        var plan = new QueryPlan([new CteDefinition.NotReferencedSource(103, null, null)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- no source-type or param filter inside the NOT EXISTS
        emitted.Sql.ShouldContain(
            "      AND NOT EXISTS (\n" +
            "        SELECT 1\n" +
            "        FROM dbo.ReferenceSearchParam rsp\n" +
            "        WHERE rsp.ReferenceResourceId = r.ResourceId\n" +
            "          AND rsp.ReferenceResourceTypeId = r.ResourceTypeId)\n");
        emitted.Sql.ShouldNotContain("rsp.SearchParamId");
        emitted.Sql.ShouldNotContain("rsp.ResourceTypeId =");
    }
}
