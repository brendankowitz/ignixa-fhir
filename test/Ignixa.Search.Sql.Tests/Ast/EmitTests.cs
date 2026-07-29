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
            "    SELECT TOP (1001) T1, Sid1,\n" +
            "           CAST(CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS bit) AS IsPartial\n" +
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
    public void GivenAZeroLimitIncludeStage_WhenEmitted_ThenTheLimitCompanionOverFetchesOneSentinelRowSoTruncationStaysDetectable()
    {
        // Arrange -- a zero-budget include probe. FHIR Server's SqlServerSearchService runs phase 2 of a sorted
        // search with IncludeCount = 0 (IncludeContinuationTokenSearch) meaning "return no included resources,
        // but tell me whether any exist so I can mint a nested-includes continuation token". The companion must
        // still forward the one-row truncation sentinel the body over-fetches: TOP (0) would return nothing,
        // discard the IsPartial the CASE computed, and make the probe silently answer "no", dropping every
        // overflowing include. This mirrors the legacy generator, whose include limit CTE is TOP (includeCount + 1)
        // even when includeCount is 0.
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
            Limit: 0);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Includes: [stage]);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the body over-fetches one row (TOP (1)); the companion forwards that sentinel (TOP (1), never
        // TOP (0)) and flags partiality when the body held more than the zero budget (COUNT_BIG(*) OVER() > 0), so
        // a caller can still detect that included resources exist.
        emitted.Sql.ShouldContain(
            "inc0 AS (\n" +
            "    SELECT DISTINCT TOP (1) r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n");
        emitted.Sql.ShouldContain(
            "inc0lim AS (\n" +
            "    SELECT TOP (1) T1, Sid1,\n" +
            "           CAST(CASE WHEN COUNT_BIG(*) OVER() > 0 THEN 1 ELSE 0 END AS bit) AS IsPartial\n" +
            "    FROM inc0\n" +
            "    ORDER BY T1 ASC, Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldNotContain("TOP (0)");
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
            "    SELECT TOP (1001) T1, Sid1,\n" +
            "           CAST(CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS bit) AS IsPartial\n" +
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
            "ORDER BY sk0.Text ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenASortWithAPageBoundary_WhenEmitted_ThenTheSeekPredicateAppearsInTheWhereClause()
    {
        // Arrange -- Patient?_sort=name, second page. A custom sort pages on a typeless boundary: its
        // ORDER BY is (Text, Sid1), so the seek must break its final tie on Sid1 alone.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "WHERE (sk0.Text > @p1\n" +
            "       OR (sk0.Text = @p1 AND m.Sid1 > @p2))\n" +
            "ORDER BY sk0.Text ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(3);
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "Adams"));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", 5000L));
    }

    [Fact]
    public void GivenATypelessPageWithASingleCustomSortKey_WhenEmitted_ThenTheSeekOmitsTheTypeColumnAndTheOrderByOmitsTheTypeTiebreak()
    {
        // Arrange -- a multi-type _sort=name continuation page. The boundary carries no resource type
        // (BoundaryResourceTypeId null), mirroring the legacy custom-sort token [sortValue, surrogateId].
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the seek's final branch compares only m.Sid1, never m.T1, and the ORDER BY tiebreak is
        // Sid1 alone so it agrees with that type-free seek.
        emitted.Sql.ShouldContain(
            "WHERE (sk0.Text > @p1\n" +
            "       OR (sk0.Text = @p1 AND m.Sid1 > @p2))\n" +
            "ORDER BY sk0.Text ASC, m.Sid1 ASC");
        // The identity SELECT still projects m.T1 (the router needs the type back); what must be absent is
        // any reference to the type column in the seek or the ORDER BY.
        emitted.Sql.ShouldNotContain("m.T1 =");
        emitted.Sql.ShouldNotContain("m.T1 >");
        emitted.Sql.ShouldNotContain("m.T1 ASC");
        emitted.Parameters.Count.ShouldBe(3);
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "Adams"));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", 5000L));
    }

    [Fact]
    public void GivenATypelessPageWithNoSortKeys_WhenEmitted_ThenItIsRejected()
    {
        // Arrange -- a typeless boundary with no sort at all. A sortless search orders by (T1, Sid1), so a
        // Sid1-only seek would disagree with that type-major ORDER BY and page unsoundly. Only a custom sort
        // makes the ORDER BY type-free, so a typeless page without one must be refused, not emitted.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var page = new PageSpec([], BoundaryResourceTypeId: null, new SqlParameterRef(7000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Page: page);

        // Act / Assert
        var ex = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
        ex.Message.ShouldContain("typeless");
        ex.Message.ShouldContain("custom");
    }

    [Fact]
    public void GivenATypelessPageWithAResourceTypeSortKey_WhenEmitted_ThenItIsRejected()
    {
        // Arrange -- a _type sort orders by the very column a typeless seek omits, so the two disagree on
        // row order; the combination must be refused rather than emitted as unsound SQL.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(null, SortKeyKind.ResourceType, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef((short)103)], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act / Assert
        var ex = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
        ex.Message.ShouldContain("typeless");
        ex.Message.ShouldContain("ResourceType");
    }

    [Fact]
    public void GivenATypedPageWithACustomSortKey_WhenEmitted_ThenItIsRejected()
    {
        // Arrange -- the mirror of the typeless guard, and unsound for the mirrored reason. A custom sort
        // makes EmitOrderBy drop the m.T1 tiebreak (ordering by (Text, Sid1)) whatever the boundary looks
        // like, while a type on the boundary still makes EmitSeekPredicate emit a type-major seek. In a
        // multi-type search a row of a lower type id but higher surrogate id then sorts after the boundary
        // yet is excluded by "m.T1 > @t", and vanishes at the page seam. Only a single-type search hid this,
        // because m.T1 is constant there.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act / Assert
        var ex = Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
        ex.Message.ShouldContain("typed keyset Page");
        ex.Message.ShouldContain("custom");
        ex.Message.ShouldContain("silently dropped at the page seam");
    }

    [Fact]
    public void GivenACustomSortQueryShape_WhenPageOneAndATypelessPageTwoAreEmitted_ThenTheirOrderByClausesAreIdentical()
    {
        // A keyset walk is sound only if every page shares one ordering. Page 1 carries no PageSpec while a
        // later page carries a typeless boundary, so were the ORDER BY decided by the boundary's presence the
        // two would order differently -- page 1 keeping m.T1, page 2 dropping it -- and rows could be skipped
        // or repeated across the page-1/page-2 seam. Because the sort is custom, both order by (Text, Sid1).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);

        var pageOnePlan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort);
        var typelessPage = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        var pageTwoPlan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: typelessPage);

        var pageOneOrderBy = LastOrderBy(SqlBuilder.Run(pageOnePlan).Sql);
        var pageTwoOrderBy = LastOrderBy(SqlBuilder.Run(pageTwoPlan).Sql);

        pageOneOrderBy.ShouldBe("ORDER BY sk0.Text ASC, m.Sid1 ASC");
        pageTwoOrderBy.ShouldBe(pageOneOrderBy);
    }

    [Fact]
    public void GivenACustomSortIncludeShape_WhenPageOneAndATypelessPageTwoAreEmitted_ThenTheOuterIncludeOrderByClausesAreIdentical()
    {
        // The include path's outer ORDER BY (EmitOuterOrderByForIncludes) must honour the same invariant: the
        // match/include union has to be ordered identically on page 1 (no boundary) and on a typeless page 2,
        // or the walk skips rows at the page seam. A custom sort drops the T1 tiebreak on both.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);

        var pageOnePlan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Includes: [includeStage]);
        var typelessPage = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        var pageTwoPlan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: typelessPage, Includes: [includeStage]);

        var pageOneOrderBy = LastOrderBy(SqlBuilder.Run(pageOnePlan).Sql);
        var pageTwoOrderBy = LastOrderBy(SqlBuilder.Run(pageTwoPlan).Sql);

        pageOneOrderBy.ShouldBe("ORDER BY IsMatch DESC, SortValue0 ASC, Sid1 ASC");
        pageTwoOrderBy.ShouldBe(pageOneOrderBy);
    }

    // The final (outer) ORDER BY of an emitted statement -- the one a keyset walk pages against.
    private static string LastOrderBy(string sql) =>
        sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..].TrimEnd();

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

        // Assert -- the missing-name segment of a custom sort is type-free too: its ORDER BY is m.Sid1 alone,
        // never m.T1, so a multi-type search (which has no single type to substitute into a seek) can page it.
        emitted.Sql.ShouldNotContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain(
            "SELECT TOP (10) m.T1, m.Sid1 FROM cte0 m\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202)\n" +
            "ORDER BY m.Sid1 ASC");
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
            BoundaryResourceTypeId: null,
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
            "       OR (sk0.Text = @p1 AND ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') = @p2 AND m.Sid1 > @p3))\n" +
            "ORDER BY sk0.Text ASC, ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') DESC, m.Sid1 ASC");
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
        // The trailing tiebreak's Sid1 term is suppressed here: m.Sid1 is already LastUpdated's own
        // sort-key value expression, so repeating it would name the same column twice in one ORDER BY
        // list, which SQL Server rejects with Msg 145 -- see SqlBuilder.EmitOrderBy.
        emitted.Sql.ShouldContain("ORDER BY m.Sid1 DESC, m.T1 ASC");
    }

    [Fact]
    public void GivenALastUpdatedOnlySort_WhenEmitted_ThenTheOrderByNamesTheSurrogateIdColumnExactlyOnce()
    {
        // Arrange -- Patient?_sort=_lastUpdated. SortValueExpr(LastUpdated) is literally "m.Sid1", which
        // is also the trailing keyset tiebreak column. Appending both produces "ORDER BY m.Sid1 ASC,
        // m.T1 ASC, m.Sid1 ASC" -- rejected at execution time by SQL Server Msg 145, "A column has been
        // specified more than once in the order by list." Only executing the SQL surfaces this, so it is
        // pinned here as a text-level invariant.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        var orderBy = emitted.Sql[emitted.Sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
        orderBy.Split("m.Sid1", StringSplitOptions.None).Length.ShouldBe(2);
        orderBy.ShouldBe("ORDER BY m.Sid1 ASC, m.T1 ASC");
    }

    [Fact]
    public void GivenATypeAndLastUpdatedSort_WhenEmitted_ThenTheOrderByNamesBothColumnsExactlyOnceAndKeepsTheirDirections()
    {
        // Arrange -- Patient?_sort=-_type,-_lastUpdated. Both keys' value expressions are themselves the
        // trailing keyset tiebreak columns ("m.T1" and "m.Sid1"), so appending the tiebreak unconditionally
        // would name each twice, which SQL Server rejects with Msg 145. Worse than illegal, the appended
        // terms are hard-coded ASC, so a descending sort would silently be contradicted. Only executing the
        // SQL surfaces either problem, so both are pinned here as text-level invariants.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec(
            [
                new SortKey(null, SortKeyKind.ResourceType, SortOrder.Descending),
                new SortKey(null, SortKeyKind.LastUpdated, SortOrder.Descending),
            ],
            SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- neither key contributes a join: the match set already projects both columns.
        emitted.Sql.ShouldNotContain("JOIN dbo.");
        var orderBy = emitted.Sql[emitted.Sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];
        orderBy.Split("m.T1", StringSplitOptions.None).Length.ShouldBe(2);
        orderBy.Split("m.Sid1", StringSplitOptions.None).Length.ShouldBe(2);
        orderBy.ShouldBe("ORDER BY m.T1 DESC, m.Sid1 DESC");
    }

    [Fact]
    public void GivenATypeAndLastUpdatedSortWithAPageBoundary_WhenEmitted_ThenTheSeekPredicateStepsThroughBothKeys()
    {
        // Arrange -- page two of Patient?_sort=_type,_lastUpdated. The boundary must carry one value per
        // active key, and those values are the same (ResourceTypeId, ResourceSurrogateId) pair the
        // continuation token already holds -- which is exactly what makes this sort keyset-pageable.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec(
            [
                new SortKey(null, SortKeyKind.ResourceType, SortOrder.Ascending),
                new SortKey(null, SortKeyKind.LastUpdated, SortOrder.Ascending),
            ],
            SortPhase.Valued);
        var page = new PageSpec(
            [new SqlParameterRef((short)103), new SqlParameterRef(5000L)],
            new SqlParameterRef((short)103),
            new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort, Page: page);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the lexicographic branches over the two keys, then the (T1, Sid1) tiebreak branches.
        // The tiebreak branches are logically dead here (their all-equal prefix already pins both columns),
        // but EmitSeekPredicate appends them uniformly rather than special-casing resource-column keys.
        emitted.Sql.ShouldContain("m.T1 > @p1");
        emitted.Sql.ShouldContain("(m.T1 = @p1 AND m.Sid1 > @p2)");
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
            "    ORDER BY sk0.Text ASC, m.Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldContain(
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, SortValue0 FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial, NULL FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC, SortValue0 ASC, Sid1 ASC");
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
        // The boundary is typeless because the sort is custom; a typed one would be refused by
        // RejectUnsupportedCombinations first and this test would stop exercising the count guard at all.
        var page = new PageSpec([new SqlParameterRef("Zorro")], BoundaryResourceTypeId: null, new SqlParameterRef(9000L));
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
        // The boundary is typeless because the sort is custom; a typed one would be refused by
        // RejectUnsupportedCombinations first and this test would stop exercising the count guard at all.
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.MissingPrimary);
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
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
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
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
            "       OR (sk0.Text = @p1 AND m.Sid1 > @p2))\n" +
            "    ORDER BY sk0.Text ASC, m.Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldContain(
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, SortValue0 FROM cteMatchPage");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, SortValue0 ASC, Sid1 ASC");
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
            "    ORDER BY m.Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldContain(
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )");
        emitted.Sql.ShouldContain("SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, Sid1 ASC");
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
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
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
        // exact "WHERE {outer} AND (...)" text would not appear -- the trailing OR branch would
        // instead sit at the top level, bypassing r.ResourceId = @p1 entirely.
        emitted.Sql.ShouldContain(
            "WHERE r.ResourceId = @p1 AND (sk0.Text > @p2\n" +
            "       OR (sk0.Text = @p2 AND m.Sid1 > @p3))\n" +
            "ORDER BY sk0.Text ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(4);
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "123"));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", "Adams"));
    }

    [Fact]
    public void GivenTheMissingPrimaryPhaseWithAMultiBranchPageBoundary_WhenEmitted_ThenTheNotExistsFilterAppliesToEveryBranchOfTheParenthesizedSeekPredicate()
    {
        // Arrange -- Patient?_sort=name,-birthdate, missing-name phase, second page: a two-key sort so
        // the MissingPrimary phase's seek predicate is a multi-branch OR chain (one active-key level plus
        // the surrogate-id tie-break branch), not the single-branch degenerate case -- proving NOT EXISTS
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
            BoundaryResourceTypeId: null,
            new SqlParameterRef(9000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- before the fix, this "NOT EXISTS(...) AND (branch0 OR branch1)" text would not exist:
        // NOT EXISTS would only bind to branch0 via AND, and branch1 would sit at the top level
        // unfiltered, letting rows WITH a name value (that NOT EXISTS was meant to exclude) leak into the
        // missing-name phase's page 2+ results.
        emitted.Sql.ShouldContain(
            "WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202) " +
            "AND (ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') < @p1\n" +
            "       OR (ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') = @p1 AND m.Sid1 > @p2))\n" +
            "ORDER BY ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') DESC, m.Sid1 ASC");
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
            "WHERE r.ResourceId = @p1");
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
        emitted.Sql.ShouldContain("WHERE NOT ((r.ResourceId = @p1 OR r.ResourceId = @p2))");
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

    [Fact]
    public void GivenAPlanThatIncludesHistory_WhenEmitted_ThenNoIsHistoryFilterIsApplied()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Visibility: new ResourceVisibility(IsHistory: null, IsDeleted: false));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldNotContain("IsHistory = 0");
        sql.ShouldContain("IsDeleted = 0");
    }

    [Fact]
    public void GivenAPlanWithDefaultVisibility_WhenEmitted_ThenBothCurrentRowFiltersAreApplied()
    {
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("IsHistory = 0");
        sql.ShouldContain("IsDeleted = 0");
    }

    [Fact]
    public void GivenAForwardChainJoinWithFullyRelaxedVisibility_WhenEmitted_ThenNoRowFiltersAreInTheResourceJoin()
    {
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), ResourceTypeId: 105, SearchParamId: 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Acme"))),
                new CteDefinition.ChainJoin(new CteRef(0), ReferenceSearchParamId: 55, InnerResourceTypeId: 105, OutputResourceTypeIds: [103], ChainDirection.Forward),
            ],
            new CteRef(1),
            Visibility: new ResourceVisibility(IsHistory: null, IsDeleted: null));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldNotContain("IsHistory = 0");
        sql.ShouldNotContain("IsDeleted = 0");
        // JOIN to Resource and the inner match must still be present
        sql.ShouldContain("INNER JOIN dbo.Resource r");
        sql.ShouldContain("INNER JOIN cte0 m");
    }

    [Fact]
    public void GivenANotReferencedSourceWithFullyRelaxedVisibility_WhenEmitted_ThenNoRowFiltersAreInTheWhereClause()
    {
        var plan = new QueryPlan(
            [new CteDefinition.NotReferencedSource(103, 96, 969)],
            new CteRef(0),
            Visibility: new ResourceVisibility(IsHistory: null, IsDeleted: null));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldNotContain("IsHistory = 0");
        sql.ShouldNotContain("IsDeleted = 0");
        sql.ShouldContain("FROM dbo.Resource r");
        sql.ShouldContain("r.ResourceTypeId = @p0");
    }

    [Fact]
    public void GivenAPlanWithAProjection_WhenEmitted_ThenTheTerminalSelectReturnsTheNamedResourceColumns()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Projection: new ProjectionSpec(["ResourceId", "Version", "RawResource", "IsDeleted"]));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("r.[ResourceId]");
        sql.ShouldContain("r.[RawResource]");
        sql.ShouldContain("INNER JOIN dbo.Resource r");
    }

    [Fact]
    public void GivenAPlanWithNoProjection_WhenEmitted_ThenTheTerminalSelectReturnsIdentityColumnsOnly()
    {
        var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldNotContain("RawResource");
    }

    [Fact]
    public void GivenACountOnlyPlanWithAProjection_WhenEmitted_ThenTheProjectionIsIgnored()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            CountOnly: true,
            Projection: new ProjectionSpec(["RawResource"]));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("COUNT_BIG(DISTINCT");
        sql.ShouldNotContain("RawResource");
    }

    [Fact]
    public void GivenAPlanWithProjectionAndOuterPredicate_WhenEmitted_ThenTheResourceJoinAppearsExactlyOnce()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123")),
            Projection: new ProjectionSpec(["RawResource", "IsDeleted"]));

        var sql = SqlBuilder.Run(plan).Sql;

        System.Text.RegularExpressions.Regex.Matches(sql, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAPlanWithAnEmptyProjection_WhenEmitted_ThenItBehavesAsIfNoProjectionWasSpecified()
    {
        // An empty column list is treated as equivalent to null — projecting zero columns is the same
        // as asking for identity-only output, and avoids emitting a dangling comma in the SELECT list.
        var planWithEmpty = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0), Projection: new ProjectionSpec([]));
        var planWithNull = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0));

        var sqlWithEmpty = SqlBuilder.Run(planWithEmpty).Sql;
        var sqlWithNull = SqlBuilder.Run(planWithNull).Sql;

        sqlWithEmpty.ShouldBe(sqlWithNull);
    }

    [Fact]
    public void GivenAPlanWithASurrogateIdRange_WhenEmitted_ThenBothBoundsAreBoundParameters()
    {
        // Arrange -- plain no-includes shape; range alone, no outer predicate.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert -- inclusive bounds (>= / <=, not > / <); values are parameters, never literals.
        emitted.Sql.ShouldContain("m.Sid1 >=");
        emitted.Sql.ShouldContain("m.Sid1 <=");
        emitted.Parameters.Select(p => p.Value).ShouldContain(5000L);
        emitted.Parameters.Select(p => p.Value).ShouldContain(6000L);
    }

    [Fact]
    public void GivenASurrogateIdRange_WhenEmitted_ThenBoundsAreInclusiveNotExclusive()
    {
        // The doc says "inclusive … window". The presence of >= and <= (not bare > and <) is the
        // complete proof: the emitter can only produce one form, and it is the inclusive one.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("m.Sid1 >=");
        sql.ShouldContain("m.Sid1 <=");
    }

    [Fact]
    public void GivenASurrogateIdRangeAlone_WhenEmitted_ThenNoResourceJoinIsAddedForTheBound()
    {
        // The reason B's SurrogateIdRange shape won over A's outer-predicate splice: the range renders
        // against m.Sid1 directly, so bounding a scan by surrogate id must never by itself force a
        // dbo.Resource join. The match CTE here is a ParamSource (not a ResourceSource, whose own FROM
        // legitimately mentions dbo.Resource), and there is no OuterPredicate, SearchParameterHash, or
        // Projection -- those are the other, legitimate reasons NeedsResourceJoin can add the join;
        // isolating the range from all of them is the whole point of this test.
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 103, 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith")))],
            new CteRef(0),
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("m.Sid1 >=");
        sql.ShouldContain("m.Sid1 <=");
        sql.ShouldNotContain("dbo.Resource");
    }

    [Fact]
    public void GivenACountOnlyPlanWithASurrogateIdRange_WhenEmitted_ThenTheSurrogateRangeIsAppliedToTheCountQuery()
    {
        // CountOnly shape: the WHERE clause must filter the count to this partition.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            CountOnly: true,
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain("COUNT_BIG(DISTINCT m.Sid1)");
        emitted.Sql.ShouldContain("m.Sid1 >=");
        emitted.Sql.ShouldContain("m.Sid1 <=");
        emitted.Parameters.Select(p => p.Value).ShouldContain(5000L);
        emitted.Parameters.Select(p => p.Value).ShouldContain(6000L);
    }

    [Fact]
    public void GivenACountOnlyPlanWithOuterPredicateAndSurrogateIdRange_WhenEmitted_ThenBothFiltersAppearInTheWhere()
    {
        // CountOnly + outer predicate + range: all three must coexist in the WHERE clause.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outerPredicate = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("abc"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            OuterPredicate: outerPredicate,
            CountOnly: true,
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r");
        emitted.Sql.ShouldContain("WHERE ");
        emitted.Sql.ShouldContain("ResourceId =");
        emitted.Sql.ShouldContain("m.Sid1 >=");
        emitted.Sql.ShouldContain("m.Sid1 <=");
    }

    [Fact]
    public void GivenANoIncludesPlanWithSortPageAndSurrogateIdRange_WhenEmitted_ThenRangeParamsAreBoundAfterPageParams()
    {
        // Sort+page shape: surrogate range clauses appear in the WHERE after the seek predicate,
        // and their @pN ordinals follow the seek predicate's ordinals.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            Page: page,
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(1_000_000L), new SqlParameterRef(2_000_000L)));

        var emitted = SqlBuilder.Run(plan);

        // Range params must be bound *after* the seek params. The seek predicate allocates @p1-@p2;
        // the range must follow, not precede, them — verified by comparing value indices rather than
        // relying on absolute ordinals (which would break if seek param count ever changes).
        var allValues = emitted.Parameters.Select(p => p.Value).ToList();
        var rangeStartIdx = allValues.IndexOf(1_000_000L);
        var seekParamIdx = allValues.IndexOf("Adams"); // first seek param value
        rangeStartIdx.ShouldBeGreaterThan(seekParamIdx);
        emitted.Parameters.Select(p => p.Value).ShouldContain(1_000_000L);
        emitted.Parameters.Select(p => p.Value).ShouldContain(2_000_000L);
        emitted.Sql.ShouldContain("m.Sid1 >=");
        emitted.Sql.ShouldContain("m.Sid1 <=");
    }

    // ─── SearchParameterHash tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GivenAPlanWithASearchParameterHash_WhenEmitted_ThenRowsCarryingThatHashAreExcluded()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain("SearchParamHash");
        emitted.Parameters.Select(p => p.Value).ShouldContain("abc123");
    }

    [Fact]
    public void GivenAPlanWithASearchParameterHash_WhenEmitted_ThenTheIsNullDisjunctIsPresent_SoNeverIndexedResourcesQualify()
    {
        // A resource with a NULL SearchParamHash has never been indexed and must qualify for reindex.
        // The IS NULL disjunct is not optional: dropping it would silently skip exactly the resources
        // most in need of indexing.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("(r.SearchParamHash IS NULL OR r.SearchParamHash <> ");
    }

    [Fact]
    public void GivenAPlanWithASearchParameterHashAlone_WhenEmitted_ThenTheResourceJoinAppearsExactlyOnce()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        System.Text.RegularExpressions.Regex.Matches(sql, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAPlanWithBothAProjectionAndAHashFilter_WhenEmitted_ThenTheResourceJoinAppearsOnce()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Projection: new ProjectionSpec(["RawResource"]),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        System.Text.RegularExpressions.Regex.Matches(sql, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
        sql.ShouldContain("SearchParamHash");
        sql.ShouldContain("r.[RawResource]");
    }

    [Fact]
    public void GivenAPlanWithOuterPredicateAndHashFilter_WhenEmitted_ThenTheResourceJoinAppearsOnceAndBothFiltersArePresent()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("id99")),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        System.Text.RegularExpressions.Regex.Matches(sql, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
        sql.ShouldContain("SearchParamHash");
        sql.ShouldContain("ResourceId =");
    }

    [Fact]
    public void GivenAPlanWithProjectionOuterPredicateAndHashFilter_WhenEmitted_ThenTheResourceJoinAppearsOnceAndAllFiltersArePresent()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("id99")),
            Projection: new ProjectionSpec(["RawResource"]),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        System.Text.RegularExpressions.Regex.Matches(sql, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
        sql.ShouldContain("SearchParamHash");
        sql.ShouldContain("ResourceId =");
        sql.ShouldContain("r.[RawResource]");
    }

    [Fact]
    public void GivenACountOnlyPlanWithASearchParameterHash_WhenEmitted_ThenTheHashFilterIsAppliedToTheCount()
    {
        // A reindex driver counts outstanding work before doing it; a count that ignores the hash
        // filter would report the wrong total.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            CountOnly: true,
            SearchParameterHash: new SqlParameterRef("abc123"));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain("COUNT_BIG(DISTINCT m.Sid1)");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r");
        emitted.Sql.ShouldContain("SearchParamHash");
        emitted.Parameters.Select(p => p.Value).ShouldContain("abc123");
    }

    [Fact]
    public void GivenACountOnlyPlanWithHashFilterAlone_WhenEmitted_ThenTheResourceJoinAppearsExactlyOnce()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            CountOnly: true,
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        System.Text.RegularExpressions.Regex.Matches(sql, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
    }

    [Fact]
    public void GivenANoIncludesPlanWithSortAndSearchParameterHash_WhenEmitted_ThenBothFiltersAppearAndTheResourceJoinIsEmittedOnce()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort,
            SearchParameterHash: new SqlParameterRef("abc123"));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain("SearchParamHash");
        System.Text.RegularExpressions.Regex.Matches(emitted.Sql, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
        emitted.Parameters.Select(p => p.Value).ShouldContain("abc123");
    }

    [Fact]
    public void GivenAPlanWithSearchParameterHashAndSurrogateRange_WhenEmitted_ThenBothFiltersAppearInTheWhere()
    {
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain("m.Sid1 >=");
        emitted.Sql.ShouldContain("m.Sid1 <=");
        emitted.Sql.ShouldContain("SearchParamHash");
    }

    [Fact]
    public void GivenAnIncludesPlanWithASearchParameterHash_WhenEmitted_ThenTheHashFilterAppliesToTheMatchArmOnly()
    {
        // Reindex does not use _include: the combination is semantically meaningless. Include rows are
        // fetched by reference from matched resources and are not iterated independently for reindexing.
        // Applying the hash filter to include rows would drop legitimately-included resources whose hash
        // differs from the current definition set but which are not themselves being reindexed.
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var emitted = SqlBuilder.Run(plan);

        // The filter must appear inside the cteMatchPage CTE only, not in the include-stage CTEs.
        var matchPageStart = emitted.Sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = emitted.Sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        matchPageStart.ShouldBeGreaterThanOrEqualTo(0);
        inc0Start.ShouldBeGreaterThanOrEqualTo(0);

        var matchPageBody = emitted.Sql[matchPageStart..inc0Start];
        matchPageBody.ShouldContain("SearchParamHash");

        var inc0End = emitted.Sql.IndexOf("inc0lim AS (", StringComparison.Ordinal);
        var inc0Body = emitted.Sql[inc0Start..inc0End];
        inc0Body.ShouldNotContain("SearchParamHash");
    }

    [Fact]
    public void GivenAnIncludesPlanWithASearchParameterHash_WhenEmitted_ThenTheResourceJoinInsideMatchPageAppearsOnce()
    {
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        // The inc0/inc0lim stages use their own dbo.Resource join for chaining, so we count only
        // the match-page INNER JOIN dbo.Resource added by the hash filter.
        var matchPageStart = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var matchPageBody = sql[matchPageStart..inc0Start];
        System.Text.RegularExpressions.Regex.Matches(matchPageBody, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAnIncludesPlanWithASearchParameterHashAndAnOuterPredicate_WhenEmitted_ThenTheResourceJoinAppearsOnce()
    {
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
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("id99")),
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        var matchPageStart = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var matchPageBody = sql[matchPageStart..inc0Start];
        System.Text.RegularExpressions.Regex.Matches(matchPageBody, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
        matchPageBody.ShouldContain("SearchParamHash");
        matchPageBody.ShouldContain("ResourceId =");
    }

    [Fact]
    public void GivenAnIncludesPlanWithOuterPredicateAndNoHash_WhenEmitted_ThenMatchPageJoinsResourceAndAppliesOuterPredicate()
    {
        // Regression guard: the join-condition change (&&) must emit the resource join when an outer
        // predicate is set even when no hash filter is present. If the guard were regressed to ||,
        // the join would be dropped silently because SearchParameterHash is null would short-circuit.
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
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("id99")),
            Includes: [stage]);

        var sql = SqlBuilder.Run(plan).Sql;

        var matchPageStart = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var matchPageBody = sql[matchPageStart..inc0Start];
        matchPageBody.ShouldContain("INNER JOIN dbo.Resource r ON");
        matchPageBody.ShouldContain("ResourceId =");
    }

    [Fact]
    public void GivenAnIncludesPlanWithASearchParameterHash_WhenEmitted_ThenHashFilterIsInsideMatchPageWhereClause()
    {
        // Prove the filter appears in cteMatchPage's WHERE, not in the outer UNION ALL assembly section.
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        var matchPageStart = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var matchPageBody = sql[matchPageStart..inc0Start];
        matchPageBody.ShouldContain("WHERE");
        matchPageBody.ShouldContain("SearchParamHash");
    }

    [Fact]
    public void GivenAnIncludesPlanWithNoHashAndNoOuterPredicate_WhenEmitted_ThenMatchPageHasNoResourceJoin()
    {
        // Regression guard: the includes shape must not emit a resource join in cteMatchPage when
        // neither the outer predicate nor the hash filter is present.
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

        var sql = SqlBuilder.Run(plan).Sql;

        var matchPageStart = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var matchPageBody = sql[matchPageStart..inc0Start];
        // The inc0 stage has its own resource join; we must not mistake it for the match-page join.
        matchPageBody.ShouldNotContain("INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1");
    }

    [Fact]
    public void GivenAnIncludesPlanWithAnIncludesPlanWithHashFilter_WhenEmitted_ThenTheUnionAllAssemblyDoesNotContainTheHashFilter()
    {
        // The hash filter is scoped to the match arm inside cteMatchPage. The outer UNION ALL assembly
        // (the SELECT ... FROM cteMatchPage block) must not have it: include rows should flow through
        // without any hash restriction.
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        // Extract from the start of the UNION ALL assembly (after the last CTE definition)
        var inc0LimEnd = sql.IndexOf("\nSELECT", sql.IndexOf("inc0lim AS (", StringComparison.Ordinal), StringComparison.Ordinal);
        var assemblySection = sql[inc0LimEnd..];
        assemblySection.ShouldNotContain("SearchParamHash");
    }

    [Fact]
    public void GivenAnIncludesPlanWithASearchParameterHash_WhenEmitted_ThenIncludeStageDoesNotCarryHashFilter()
    {
        // Proves inc0 body does not contain SearchParamHash (complementary to match-arm test).
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var inc0LimStart = sql.IndexOf("inc0lim AS (", StringComparison.Ordinal);
        var inc0Body = sql[inc0Start..inc0LimStart];
        inc0Body.ShouldNotContain("SearchParamHash");
    }

    [Fact]
    public void GivenAnIncludesPlanWithAnIncludesPlanWithHashFilter_WhenEmitted_ThenBothSearchParamHashAndIncludeJoinArePresent()
    {
        // Confirms the includes shape with hash compiles without corrupting the include join logic.
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("SearchParamHash");
        sql.ShouldContain("inc0 AS (");
        sql.ShouldContain("inc0lim AS (");
        sql.ShouldContain("UNION ALL");
    }

    [Fact]
    public void GivenAnIncludesPlanWithAnIncludesPlanWithHashFilter_WhenEmitted_ThenExistsCorrelationStillPointsToMatchPage()
    {
        // The EXISTS correlation in inc0 must still reference cteMatchPage — not a broken alias.
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
    }

    [Fact]
    public void GivenAnIncludesPlanWithAnIncludesPlanWithHashFilterAndSurrogateRange_WhenEmitted_ThenBothFiltersAreInsideMatchPage()
    {
        // Both hash and surrogate range must be scoped to the match arm.
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
            Includes: [stage],
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        var matchPageStart = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var matchPageBody = sql[matchPageStart..inc0Start];
        matchPageBody.ShouldContain("SearchParamHash");
        matchPageBody.ShouldContain("Sid1 >=");
        matchPageBody.ShouldContain("Sid1 <=");
    }

    [Fact]
    public void GivenAnIncludesPlanWithNoHashFilter_WhenEmitted_ThenExistingBehaviourIsUnchanged()
    {
        // Regression guard: adding SearchParameterHash support must not change the SQL for plans
        // that do not set it.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Includes: [stage]);

        var emitted = SqlBuilder.Run(plan);

        // Exact known-good text from the corresponding GivenAForwardIncludeStageSeededFromMatch test.
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
            "    SELECT TOP (1001) T1, Sid1,\n" +
            "           CAST(CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS bit) AS IsPartial\n" +
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
    public void GivenAnIncludesPlanWithASearchParameterHash_WhenEmitted_ThenIncludeBodyPreservesIsNullDisjunct()
    {
        // Prove the IS NULL guard in the hash filter is present for the includes/match-arm shape too.
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("(r.SearchParamHash IS NULL OR r.SearchParamHash <> ");
    }

    [Fact]
    public void GivenAnIncludesPlanWithAnIncludesPlanWithHashFilter_WhenEmitted_ThenIncludeLimitBodyDoesNotContainHashFilter()
    {
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        var inc0LimStart = sql.IndexOf("inc0lim AS (", StringComparison.Ordinal);
        var inc0LimEnd = sql.IndexOf("\n)\n", inc0LimStart, StringComparison.Ordinal);
        var inc0LimBody = sql[inc0LimStart..inc0LimEnd];
        inc0LimBody.ShouldNotContain("SearchParamHash");
    }

    [Fact]
    public void GivenAnIncludesPlanWithAnIncludesPlanWithHashFilter_WhenEmitted_ThenNotExistsInFinalSelectDoesNotContainHashFilter()
    {
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        // The NOT EXISTS in "SELECT i.T1 ... WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage ...)" should
        // not contain SearchParamHash; it is just a deduplication predicate.
        var notExistsIdx = sql.IndexOf("WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage", StringComparison.Ordinal);
        notExistsIdx.ShouldBeGreaterThanOrEqualTo(0);
        var notExistsClause = sql[notExistsIdx..(notExistsIdx + 80)];
        notExistsClause.ShouldNotContain("SearchParamHash");
    }

    [Fact]
    public void GivenANoIncludesPlanWithHashFilterAndNoOtherJoinTrigger_WhenEmitted_ThenNoResourceJoinExistsWithoutHash()
    {
        // Regression: prove no-hash plan still has no resource join.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0));

        var sql = SqlBuilder.Run(plan).Sql;

        // The ResourceSource CTE itself references dbo.Resource inside the CTE block;
        // the outer SELECT must not add an extra INNER JOIN.
        sql.ShouldNotContain("INNER JOIN dbo.Resource r");
    }

    [Fact]
    public void GivenAPlanWithSearchParameterHashOnly_WhenEmitted_ThenHashValueIsNeverInlinedInSql()
    {
        // Safety: the hash value must only appear as a parameter, never as a SQL literal.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            SearchParameterHash: new SqlParameterRef("MyHashValue"));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldNotContain("MyHashValue");
        emitted.Parameters.Select(p => p.Value).ShouldContain("MyHashValue");
    }

    [Fact]
    public void GivenAnIncludesPlanWithHashFilterAndNoSortAndNoTop_WhenEmitted_ThenMatchPageHasNoOrderByButOuter()
    {
        // Regression guard: the includes + no-sort + no-top constraint (SQL Server Msg 1033) must still
        // hold when hash filter is added.
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
            Includes: [stage],
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        var matchPageStart = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var matchPageBody = sql[matchPageStart..inc0Start];
        matchPageBody.ShouldNotContain("ORDER BY");
        sql.ShouldEndWith("ORDER BY IsMatch DESC, T1 ASC, Sid1 ASC");
    }

    [Fact]
    public void GivenAnIncludesPlanWithAnIncludesPlanWithHashFilterAndAnOuterPredicateAndSurrogateRange_WhenEmitted_ThenAllThreeFiltersAreInsideMatchPageWhereClause()
    {
        // All three post-join filters coexist correctly in cteMatchPage's WHERE.
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
            OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("id99")),
            Includes: [stage],
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)),
            SearchParameterHash: new SqlParameterRef("abc123"));

        var sql = SqlBuilder.Run(plan).Sql;

        var matchPageStart = sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        var matchPageBody = sql[matchPageStart..inc0Start];
        matchPageBody.ShouldContain("SearchParamHash");
        matchPageBody.ShouldContain("Sid1 >=");
        matchPageBody.ShouldContain("Sid1 <=");
        matchPageBody.ShouldContain("ResourceId =");
        System.Text.RegularExpressions.Regex.Matches(matchPageBody, "INNER JOIN dbo.Resource r").Count.ShouldBe(1);
    }

    // ─── End SearchParameterHash tests ──────────────────────────────────────────────────────────────

    // ─── Ordinal invariant guard ────────────────────────────────────────────────────────────────────

    [Fact]
    public void GivenAPlanWithCteAndShapeBoundParameters_WhenEmitted_ThenCteParametersOccupyTheLeadingOrdinals()
    {
        // PlanExplainer reads parameters back by ordinal, so EmitCteBlocks must bind every CTE parameter
        // before any shape binds one of its own. Nothing but this test catches a shape's binding logic
        // being hoisted ahead of the CTE prelude -- the failure mode otherwise is a large, unexplained
        // golden-SQL diff rather than a named assertion. cte0's predicate value must land at @p0; the
        // shape-level OuterPredicate and SearchParameterHash values must follow, in shape emission order.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var ctePredicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outerPredicate = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("abc"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, ctePredicate)],
            new CteRef(0),
            OuterPredicate: outerPredicate,
            SearchParameterHash: new SqlParameterRef("hash123"));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Parameters.Select(p => p.Name).ShouldBe(["@p0", "@p1", "@p2"]);
        emitted.Parameters.Select(p => p.Value).ShouldBe(["Smith", "abc", "hash123"]);
    }

    // ─── End ordinal invariant guard ────────────────────────────────────────────────────────────────

    // ─── OffsetPage tests ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GivenANoIncludesPlanWithOffsetPage_WhenEmitted_ThenEmitsOffsetFetchAfterOrderBy()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            OffsetPage: new OffsetSpec(20, 10));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC\n" +
            "OFFSET @p1 ROWS FETCH NEXT @p2 ROWS ONLY");
        emitted.Parameters.Select(p => p.Name).ShouldBe(["@p0", "@p1", "@p2"]);
        emitted.Parameters.Select(p => p.Value).ShouldBe(["Smith", 20, 10]);
    }

    [Fact]
    public void GivenAnIncludesPlanWithOffsetPage_WhenEmitted_ThenMatchPageCteEmitsOrderByAndOffsetFetch()
    {
        // Arrange -- no Top, so cteMatchPage would normally omit its own ORDER BY (SQL Server Msg 1033
        // forbids one without TOP); OffsetPage must gate that ORDER BY just as Top does, since OFFSET/FETCH
        // is equally illegal without one.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            OffsetPage: new OffsetSpec(20, 10));

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldContain(
            "cteMatchPage AS (\n" +
            "    SELECT m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "    ORDER BY m.T1 ASC, m.Sid1 ASC\n" +
            "    OFFSET @p1 ROWS FETCH NEXT @p2 ROWS ONLY\n" +
            ")");
    }

    [Fact]
    public void GivenACountOnlyPlanWithOffsetPage_WhenEmitted_ThenOffsetFetchIsNotEmitted()
    {
        // CountOnly returns a single scalar row; an OFFSET/FETCH clause has nothing to page there and
        // CountOnly already deliberately ignores Sort/Page for the same reason (see EmitCountOnlyShape).
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            CountOnly: true,
            OffsetPage: new OffsetSpec(20, 10));

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldNotContain("OFFSET");
        sql.ShouldNotContain("FETCH");
    }

    // ─── End OffsetPage tests ───────────────────────────────────────────────────────────────────────

    // ─── CountPhaseScoped tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GivenACountOnlyPlanWithCountPhaseScoped_WhenEmitted_ThenCountJoinsThePhasesOwnSortKey()
    {
        // Arrange -- Valued phase: Keys[0]'s join is present and the count must scope to it, not the
        // whole match set, or a two-phase executor would double count rows present in both phases.
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            CountOnly: true,
            Sort: sort,
            CountPhaseScoped: true);

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.Resource\n" +
            "    WHERE ResourceTypeId = @p0 AND IsHistory = 0 AND IsDeleted = 0\n" +
            ")\n" +
            "SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m\n" +
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 202 AND sk0.IsMin = 1");
    }

    [Fact]
    public void GivenACountOnlyPlanWithCountPhaseScopedAndMissingPrimaryPhase_WhenEmitted_ThenWhereExcludesRowsCarryingTheKey()
    {
        // Arrange -- MissingPrimary phase: Keys[0] is excluded from the joins (EmitSortJoins' own
        // MissingPrimary continue) and instead the count must apply the NOT EXISTS filter, the same
        // predicate the match shapes use for this phase.
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.MissingPrimary);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            CountOnly: true,
            Sort: sort,
            CountPhaseScoped: true);

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m\n");
        sql.ShouldNotContain("INNER JOIN dbo.StringSearchParam sk0");
        sql.ShouldContain(
            "WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 " +
            "AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202)");
    }

    [Fact]
    public void GivenACountOnlyPlanWithoutCountPhaseScoped_WhenEmitted_ThenSortIsIgnoredAsBefore()
    {
        // Regression guard: CountOnly without CountPhaseScoped must keep ignoring Sort entirely -- no
        // join, no MissingPrimary filter -- exactly as EmitCountOnlyShape's remarks already document.
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            CountOnly: true,
            Sort: sort);

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldNotContain("StringSearchParam sk0");
        sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.Resource\n" +
            "    WHERE ResourceTypeId = @p0 AND IsHistory = 0 AND IsDeleted = 0\n" +
            ")\n" +
            "SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m");
    }

    // ─── End CountPhaseScoped tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GivenAnIncludesPlanWithASurrogateIdRange_WhenEmitted_ThenTheRangeAppliesOnlyToTheMatchArm()
    {
        // The range constrains the match partition only — include rows are fetched by reference and
        // must not be filtered by the match partition's surrogate window (that would drop legitimate
        // includes whose surrogate IDs fall outside the window even though they reference a matched resource).
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
            Includes: [stage],
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)));

        var emitted = SqlBuilder.Run(plan);

        // Match arm carries the range filter; include-stage CTEs do not.
        emitted.Sql.ShouldContain("m.Sid1 >=");
        emitted.Sql.ShouldContain("m.Sid1 <=");
        emitted.Parameters.Select(p => p.Value).ShouldContain(5000L);
        emitted.Parameters.Select(p => p.Value).ShouldContain(6000L);

        // Range must appear inside the cteMatchPage CTE, not after it in the UNION ALL assembly.
        // Extract the body between "cteMatchPage AS (" and the next "inc0 AS (" block.
        var matchPageStart = emitted.Sql.IndexOf("cteMatchPage AS (", StringComparison.Ordinal);
        var inc0Start = emitted.Sql.IndexOf("inc0 AS (", StringComparison.Ordinal);
        matchPageStart.ShouldBeGreaterThanOrEqualTo(0);
        inc0Start.ShouldBeGreaterThanOrEqualTo(0);
        var matchPageBody = emitted.Sql[matchPageStart..inc0Start];
        matchPageBody.ShouldContain("Sid1 >=");
        matchPageBody.ShouldContain("Sid1 <=");

        // Include-stage CTE (inc0) must not mention the range.
        var inc0End = emitted.Sql.IndexOf("inc0lim AS (", StringComparison.Ordinal);
        inc0End.ShouldBeGreaterThanOrEqualTo(0);
        var inc0Body = emitted.Sql[inc0Start..inc0End];
        inc0Body.ShouldNotContain("Sid1 >=");
        inc0Body.ShouldNotContain("Sid1 <=");
    }

    // ─── IncludesOnly tests ──────────────────────────────────────────────────────────────────────────

    private static IncludeStage ForwardIncludeStage(short seedType, short outputType, int limit)
        => new(IncludeDirection.Forward, ReferenceSearchParamId: 210, SeedTypeIds: [seedType], OutputTypeIds: [outputType],
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: limit);

    [Fact]
    public void GivenAPlanWithIncludes_WhenEmitted_ThenEveryUnionArmTypesIsPartialAsBit()
    {
        // Regression: the match arm emitted CAST(0 AS bit) AS IsPartial while the include arm's limit stage
        // emitted a bare CASE ... THEN 1 ELSE 0 END, which is int. T-SQL union type precedence promotes a
        // bit/int union to int, so the result column's type silently depended on whether the plan carried
        // includes. A caller reading the documented (T1, Sid1, IsMatch, IsPartial) contract as a bit then threw
        // InvalidCastException on include rows only -- the kind of defect that appears at execution against a
        // real server and is invisible to a grammar check, since the SQL parses either way.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)]);

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("CAST(CASE WHEN COUNT_BIG(*) OVER() > 10 THEN 1 ELSE 0 END AS bit) AS IsPartial");
        sql.ShouldNotContain("END AS IsPartial");
    }

    [Fact]
    public void GivenAnIncludesOnlyPlan_WhenEmitted_ThenMatchRowsAreExcludedFromTheResult()
    {
        // Arrange -- the $includes second-page scenario: caller already has the match rows.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            IncludesOnly: true);

        // Act
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- IsMatch column is still present (from the include arm's explicit alias),
        // but the match arm that emits CAST(1 AS bit) AS IsMatch must be absent.
        sql.ShouldContain("IsMatch");
        sql.ShouldNotContain("CAST(1 AS bit) AS IsMatch");
    }

    [Fact]
    public void GivenAnIncludesOnlyPlan_WhenEmitted_ThenMatchPageCteIsStillEmittedAndIncludeStagesCorrelateToIt()
    {
        // The match CTE must survive — the include stages' EXISTS/NOT EXISTS correlate against it.
        // Dropping it would either break the SQL or silently change which rows the include stages produce.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("cteMatchPage AS (");
        sql.ShouldContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        sql.ShouldContain("WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)");
    }

    [Fact]
    public void GivenAnIncludesOnlyPlan_WhenEmitted_ThenResultShapeIsStillFourColumnsAndIsMatchIsZeroOnAllRows()
    {
        // A caller reads columns by ordinal; dropping the match arm must not collapse the shape to 3
        // columns or change the IsMatch ordinal position.  Every row in an includes-only result has
        // IsMatch = 0 because there are no match rows.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        // First (and only) SELECT in the assembly section: the include arm must carry an explicit alias.
        sql.ShouldContain("CAST(0 AS bit) AS IsMatch");
        // The match arm (IsMatch = 1) must be entirely absent.
        sql.ShouldNotContain("CAST(1 AS bit)");
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithTwoStages_WhenEmitted_ThenOnlyFirstIncludeArmNamesIsMatch()
    {
        // In a UNION, column names come from the first SELECT.  The first include arm must name
        // IsMatch explicitly; subsequent arms must not double-alias it (which SQL Server would accept
        // but which would make the assertion below brittle rather than structural).
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10), ForwardIncludeStage(103, 112, 10)],
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        // Exactly one "AS IsMatch" alias must appear in the whole statement — on the first include arm.
        var count = System.Text.RegularExpressions.Regex.Matches(sql, " AS IsMatch").Count;
        count.ShouldBe(1);
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithTwoStages_WhenEmitted_ThenIsMatchAliasIsOnTheFirstUnionArm()
    {
        // SQL Server takes a UNION's column names from its first SELECT. Keying the alias off
        // unionBlocks.Count == 0 (first arm appended overall) rather than i == 0 (first include-stage
        // index) ensures that any future arm inserted before the loop cannot silently break the ordinal
        // contract that callers rely on. This test verifies the alias is on the structurally-first arm,
        // not merely present somewhere in the SQL.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10), ForwardIncludeStage(103, 112, 10)],
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        // Split the entire SQL on the assembly separator — no CTE emits "AS IsMatch", so any match
        // in arms[0] must come from the first assembly arm (which is at the end of that element).
        // This is the structural check: the alias must be in the first UNION arm overall (the
        // IncludesOnly global page joins its stage arms with plain UNION, not UNION ALL, so that
        // COUNT_BIG(*) OVER() sees the same deduplicated rows as the outer DISTINCT — see
        // SqlBuilder.EmitGlobalIncludesPage), not on a subsequent arm that happens to be the first
        // *include-stage* index.
        var arms = sql.Split("\nUNION\n");

        // The alias must be on the first arm — not on a later arm, and not merely in the SQL overall.
        arms[0].ShouldContain(" AS IsMatch");
        // Subsequent arms must not re-alias it (SQL Server reads names from the first SELECT only).
        for (var i = 1; i < arms.Length; i++)
        {
            arms[i].ShouldNotContain(" AS IsMatch");
        }
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithCountOnly_WhenEmitted_ThenThrowsNotSupportedException()
    {
        // IncludesOnly requests include rows; CountOnly requests a count of match rows.
        // The two are self-contradictory and the emitter must refuse rather than emitting
        // something arbitrary (which would silently return the wrong answer).
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            IncludesOnly: true,
            CountOnly: true);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithNoIncludeStages_WhenEmitted_ThenThrowsNotSupportedException()
    {
        // IncludesOnly with no include stages can only ever return empty, which is a caller error
        // not a legitimate empty result.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            IncludesOnly: true);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithAMissingPrimarySort_WhenEmitted_ThenTheMatchSourceCarriesTheMissingValuePredicateButNothingOrdersOrSeeksOnTheSortKey()
    {
        // The measured $includes scenario: Patient?_sort=date, first (missing-date) phase. The SortPhase is a
        // filter, not an order: it bounds the match set that seeds the includes to rows with NO date value, so
        // an engine that ignored it would return the includes of the dated rows too (the very over-return the
        // FHIR Server measurement caught). The predicate must therefore appear against the match source (m.*),
        // while the include rows must still page by (T1, Sid1) -- the sort key must never reach an ORDER BY or
        // a seek.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            Sort: new SortSpec([new SortKey(203, SortKeyKind.Date, SortOrder.Ascending)], SortPhase.MissingPrimary),
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        // The phase predicate bounds the match set the includes seed from -- the filtering role, preserved.
        sql.ShouldContain("cteMatchPage AS (");
        sql.ShouldContain(
            "NOT EXISTS (SELECT 1 FROM dbo.DateTimeSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 203)");
        // The ordering role is dropped: the include rows page by (T1, Sid1), and no SortValueN column, no
        // sort-key join, and no keyset seek on the date column is emitted anywhere.
        sql.ShouldContain("ORDER BY T1 ASC, Sid1 ASC");
        sql.ShouldNotContain("SortValue");
        sql.ShouldNotContain("StartDateTime");
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithAValuedSort_WhenEmitted_ThenTheMatchSourceGatesOnTheSortValueButProjectsNoSortColumns()
    {
        // The second (valued) phase of the same sort. Here the phase filter is the primary-key INNER join --
        // it bounds the match set that seeds the includes to rows that HAVE a date value. The join must stay
        // (it is the filter), but the SortValueN columns it exists to project on an ordinary page must not:
        // an includes-only page never orders by them, so projecting them would be dead weight that implies an
        // ordering role the page does not have.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            Sort: new SortSpec([new SortKey(203, SortKeyKind.Date, SortOrder.Ascending)], SortPhase.Valued),
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        // The has-value gate that bounds the match set -- the filtering role, preserved.
        sql.ShouldContain("INNER JOIN dbo.DateTimeSearchParam sk0");
        // No ordering role: no projected sort columns, and the include rows still page by (T1, Sid1).
        sql.ShouldNotContain("SortValue");
        sql.ShouldContain("ORDER BY T1 ASC, Sid1 ASC");
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithAKeysetPage_WhenEmitted_ThenThrowsNotSupportedException()
    {
        // A sort is allowed on an includes-only page (its phase filters the match set), but a keyset Page is
        // not: EmitSeekPredicate would seek the match rows by the sort-key boundary, a second paging mechanism
        // the includes-only page does not use -- its match window is the surrogate range and its include rows
        // page from a cursor. Letting it through would let the sort key decide which resources are included, so
        // the emitter refuses it.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            Sort: new SortSpec([new SortKey(203, SortKeyKind.Date, SortOrder.Ascending)], SortPhase.Valued),
            Page: new PageSpec([new SqlParameterRef("2000-01-01")], BoundaryResourceTypeId: null, BoundarySurrogateId: new SqlParameterRef(4200L)),
            IncludesOnly: true);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithProjection_WhenEmitted_ThenProjectionColumnsAppearInIncludeArm()
    {
        // Projection must still work in IncludesOnly mode. Include rows are fetched from dbo.Resource;
        // the projection columns are added to the include arm, not the (absent) match arm.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            Projection: new ProjectionSpec(["RawResource", "IsDeleted"]),
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("r.[RawResource]");
        sql.ShouldContain("r.[IsDeleted]");
        // The match arm (which also projects) must be absent.
        sql.ShouldNotContain("CAST(1 AS bit) AS IsMatch");
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithAccessConstraints_WhenEmitted_ThenConstraintsArePreservedOnIncludeStages()
    {
        // Access constraints are a security control.  IncludesOnly must not weaken them:
        // a stage with Constraints must still emit the type-guarded EXISTS guard even in this mode.
        var constraint = new IncludeConstraint(ConstraintTypeId: 111, ConstraintCteIndex: 0);
        var stage = new IncludeStage(
            IncludeDirection.Forward,
            ReferenceSearchParamId: 210,
            SeedTypeIds: [(short)103],
            OutputTypeIds: [(short)111],
            SeedStages: [],
            SeedFromMatch: true,
            Iterate: false,
            Limit: 10,
            Constraints: [constraint]);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [stage],
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        // The constraint guard must appear in the inc0 CTE (type-guarded EXISTS / <> check).
        sql.ShouldContain($"r.ResourceTypeId <> {constraint.ConstraintTypeId} OR EXISTS (SELECT 1 FROM cte0 ac");
        // The match arm must still be absent.
        sql.ShouldNotContain("CAST(1 AS bit) AS IsMatch");
    }

    // ─── IncludesOnly global-page (cursor) tests ─────────────────────────────────────────────────────

    private static IncludeStage ReverseIncludeStage(short seedType, short outputType, int limit)
        => new(IncludeDirection.Reverse, ReferenceSearchParamId: 211, SeedTypeIds: [seedType], OutputTypeIds: [outputType],
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: limit);

    private static QueryPlan TwoStageIncludesOnlyPageWithBoundary()
        => new(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10), ReverseIncludeStage(103, 112, 10)],
            IncludesOnly: true,
            IncludeBoundary: new IncludeBoundary(111, 5000));

    [Fact]
    public void GivenAnIncludesOnlyPageWithABoundaryAndTwoStages_WhenEmitted_ThenTheBudgetIsAppliedOnceGloballyOrderedByT1Sid1()
    {
        // The $includes second page applies the row budget once across the union of every stage -- not once
        // per stage -- and resumes under (T1, Sid1). So the whole statement must carry exactly one TOP (the
        // outer global page), no per-stage limit companions, and the IsPartial window computed over the
        // union. This is the shape the FHIR Server legacy $includes page emits.
        var plan = TwoStageIncludesOnlyPageWithBoundary();

        var sql = SqlBuilder.Run(plan).Sql;

        // Exactly one TOP in the whole statement: the global page. No per-stage TOP, no incNlim companions.
        System.Text.RegularExpressions.Regex.Matches(sql, @"TOP \(").Count.ShouldBe(1);
        sql.ShouldContain("SELECT DISTINCT TOP (11) T1, Sid1, IsMatch,");
        sql.ShouldContain("CAST(CASE WHEN COUNT_BIG(*) OVER() > 10 THEN 1 ELSE 0 END AS bit) AS IsPartial");
        sql.ShouldNotContain("inc0lim");
        sql.ShouldNotContain("inc1lim");

        // Ordered by (T1, Sid1) so the resume predicate pages the union deterministically -- not the
        // matches-first order the ordinary includes shape uses.
        sql.TrimEnd().ShouldEndWith("ORDER BY T1 ASC, Sid1 ASC");
        sql.ShouldNotContain("IsMatch DESC");
    }

    /// <summary>The text of the "incN AS ( ... )" CTE block, from its opening label to its own closing paren.</summary>
    private static string IncludeStageBody(string sql, int index)
    {
        var start = sql.IndexOf($"inc{index} AS (", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);

        // A CTE closes with ")" in the first column; every paren inside the body is indented.
        var end = sql.IndexOf("\n)", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThanOrEqualTo(0);
        return sql[start..(end + 2)];
    }

    private const string GlobalResumePredicate = "(T1 > @p1 OR (T1 = @p1 AND Sid1 > @p2))";

    [Fact]
    public void GivenAnIncludesOnlyPageWithAForwardStage_WhenEmittedWithABoundary_ThenTheResumePredicateFiltersTheUnionRatherThanTheStageBody()
    {
        // The cursor is a position in the global paged output stream, not a property of any one stage's row
        // set, so it filters the union derived table on its own (T1, Sid1) -- the exact columns the outer
        // ORDER BY sees. Keeping it out of the stage body is what lets a downstream :iterate stage seed from
        // the complete body. The two cursor values still bind as parameters rather than inlining.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            IncludesOnly: true,
            IncludeBoundary: new IncludeBoundary(111, 5000));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain($") includeUnion\nWHERE {GlobalResumePredicate}");
        IncludeStageBody(emitted.Sql, 0).ShouldNotContain("@p");
        emitted.Parameters.ShouldContain(p => p.Name == "@p1" && Equals(p.Value, (short)111));
        emitted.Parameters.ShouldContain(p => p.Name == "@p2" && Equals(p.Value, 5000L));
    }

    [Fact]
    public void GivenAnIncludesOnlyPageWithAReverseStage_WhenEmittedWithABoundary_ThenTheResumePredicateStillFiltersTheUnionRatherThanTheStageBody()
    {
        // A reverse stage projects rsp.* where a forward one projects r.*, but the union derived table
        // exposes both as (T1, Sid1), so the direction no longer changes the predicate at all -- which is
        // the point: one predicate over the union cannot key on the wrong resource for one of the stages.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ReverseIncludeStage(103, 112, 10)],
            IncludesOnly: true,
            IncludeBoundary: new IncludeBoundary(112, 7000));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain($") includeUnion\nWHERE {GlobalResumePredicate}");
        IncludeStageBody(emitted.Sql, 0).ShouldNotContain("@p");
        emitted.Parameters.ShouldContain(p => p.Name == "@p1" && Equals(p.Value, (short)112));
        emitted.Parameters.ShouldContain(p => p.Name == "@p2" && Equals(p.Value, 7000L));
    }

    [Fact]
    public void GivenAnIncludesOnlyPageWithMixedStages_WhenEmittedWithABoundary_ThenTheSharedCursorIsAppliedExactlyOnceOverTheUnion()
    {
        // One cursor pages the union of all stages as a single ordered stream. Applying it once, after the
        // union, is what makes that literal: no stage can overtake another between pages, and no stage body
        // is narrowed to the rows this page happens to return.
        var plan = TwoStageIncludesOnlyPageWithBoundary();

        var emitted = SqlBuilder.Run(plan);

        System.Text.RegularExpressions.Regex
            .Matches(emitted.Sql, System.Text.RegularExpressions.Regex.Escape(GlobalResumePredicate))
            .Count.ShouldBe(1);
        emitted.Sql.ShouldContain($") includeUnion\nWHERE {GlobalResumePredicate}");
        IncludeStageBody(emitted.Sql, 0).ShouldNotContain("@p");
        IncludeStageBody(emitted.Sql, 1).ShouldNotContain("@p");
        emitted.Parameters.ShouldContain(p => p.Name == "@p1" && Equals(p.Value, (short)111));
        emitted.Parameters.ShouldContain(p => p.Name == "@p2" && Equals(p.Value, 5000L));
    }

    [Fact]
    public void GivenAnIncludesOnlyPageWithABoundaryAndAnIterateStage_WhenEmitted_ThenTheSeedReadsTheUnfilteredStageBodyNotTheAbsentLimitCompanion()
    {
        // An IncludesOnly page emits no limit companion -- the budget is global -- so an :iterate stage
        // seeding from inc0lim would reference a CTE that was never defined (SQL Server Msg 207). It seeds
        // from inc0 instead, and inc0 must stay uncursored: filtering the seed set by the page cursor would
        // make page 2 blind to iterate targets reachable only through resources page 1 already returned.
        var stage1 = new IncludeStage(
            IncludeDirection.Forward, ReferenceSearchParamId: 211, SeedTypeIds: [(short)111], OutputTypeIds: [(short)111],
            SeedStages: [0], SeedFromMatch: false, Iterate: true, Limit: 10);
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10), stage1],
            IncludesOnly: true,
            IncludeBoundary: new IncludeBoundary(111, 5000));

        var emitted = SqlBuilder.Run(plan);

        emitted.Sql.ShouldContain("SELECT 1 FROM inc0 m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        emitted.Sql.ShouldNotContain("inc0lim");
        IncludeStageBody(emitted.Sql, 1).ShouldNotContain("@p");

        System.Text.RegularExpressions.Regex
            .Matches(emitted.Sql, System.Text.RegularExpressions.Regex.Escape(GlobalResumePredicate))
            .Count.ShouldBe(1);
        emitted.Sql.ShouldContain($") includeUnion\nWHERE {GlobalResumePredicate}");
    }

    [Fact]
    public void GivenAnIncludeBoundaryWithoutIncludesOnly_WhenEmitted_ThenItIsRefusedRatherThanSilentlyDroppingIncludeRows()
    {
        // QueryPlan is a public construction surface, so SqlBuilder must guard the boundary independently of
        // Lower. Without IncludesOnly the emitter keeps the match arm and never applies the resume
        // predicate, so a caller expecting a second page would instead get a full first page back. Refuse it.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10)],
            IncludeBoundary: new IncludeBoundary(111, 5000));

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
    }

    [Fact]
    public void GivenAnIncludesOnlyPageWhoseStagesHaveDifferingLimits_WhenEmitted_ThenItIsRefusedRatherThanPagingOnAnArbitraryBudget()
    {
        // The global page applies one TOP over the union of every stage, so the budget is a property of the
        // whole ordered stream, not of any single stage. Differing per-stage limits have no single coherent
        // meaning: the emitter would silently page on includes[0].Limit and return a wrong-sized page with
        // no error. Refuse it rather than pick a budget arbitrarily.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10), ReverseIncludeStage(103, 112, 20)],
            IncludesOnly: true);

        Should.Throw<NotSupportedException>(() => SqlBuilder.Run(plan));
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithTwoStagesThatCanReachTheSameResource_WhenEmitted_ThenTheDerivedTableArmsAreJoinedByPlainUnion()
    {
        // Stands in for: a resource reachable via two different reference paths (e.g. a forward
        // Patient:organization include and a reverse Observation:subject include that both land on the
        // same (T1, Sid1) row). T-SQL evaluates COUNT_BIG(*) OVER() in the SELECT phase, before the outer
        // DISTINCT dedups its input, so if the two stage arms were joined with UNION ALL that shared row
        // would be counted twice and could wrongly flag an exactly-`budget`-sized page of distinct rows as
        // IsPartial = 1. Joining the arms with plain UNION dedups them before the window function runs, so
        // the two stages contribute exactly one row for that resource and a full page of distinct rows
        // correctly reports IsPartial = 0.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [ForwardIncludeStage(103, 111, 10), ReverseIncludeStage(103, 111, 10)],
            IncludesOnly: true);

        var sql = SqlBuilder.Run(plan).Sql;

        var unionStart = sql.IndexOf("FROM (\n", StringComparison.Ordinal);
        var unionEnd = sql.IndexOf(") includeUnion", StringComparison.Ordinal);
        unionStart.ShouldBeGreaterThanOrEqualTo(0);
        unionEnd.ShouldBeGreaterThan(unionStart);
        var derivedTable = sql[unionStart..unionEnd];

        derivedTable.ShouldContain("\nUNION\n");
        derivedTable.ShouldNotContain("UNION ALL");

        var fragment = SqlGrammar.Parse(sql);
        var unions = SqlGrammar.FindAll<Microsoft.SqlServer.TransactSql.ScriptDom.BinaryQueryExpression>(fragment);
        var derivedTableUnion = unions.ShouldHaveSingleItem();
        derivedTableUnion.BinaryQueryExpressionType.ShouldBe(Microsoft.SqlServer.TransactSql.ScriptDom.BinaryQueryExpressionType.Union);
        derivedTableUnion.All.ShouldBeFalse();
    }
}

