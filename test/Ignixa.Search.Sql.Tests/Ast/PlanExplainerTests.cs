using System.Text.RegularExpressions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
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

    [Fact]
    public void GivenAForwardChainJoin_WhenExplained_ThenRendersTheJoinShape()
    {
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), ResourceTypeId: 105, SearchParamId: 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Acme"))),
                new CteDefinition.ChainJoin(new CteRef(0), ReferenceSearchParamId: 55, InnerResourceTypeId: 105, OutputResourceTypeIds: [103], ChainDirection.Forward),
            ],
            new CteRef(1));

        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text = @p0\n" +
            "root = ChainJoin(cte0, ref=55, inner=105, output=[103], Forward)");
    }

    [Fact]
    public void GivenAPlanWithOneIncludeStage_WhenExplained_ThenAppendsAnIncLineAfterTheCteLines()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Includes: [stage]);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "matchPage = MatchPageCte(top=none, sortJoins=false, resourceJoin=false)\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)");
    }

    [Fact]
    public void GivenAWildcardIncludeStage_WhenExplained_ThenRendersStarForTheNullFields()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(IncludeDirection.Reverse, null, null, null, [], SeedFromMatch: true, Iterate: true, Limit: 500);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Includes: [stage]);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "matchPage = MatchPageCte(top=none, sortJoins=false, resourceJoin=false)\n" +
            "inc0 = IncludeStage(ref=*, seedTypes=*, outputTypes=*, seeds=[match], limit=500 iterate, Reverse)");
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithAResumeBoundary_WhenExplained_ThenTheBoundaryRowNamesTheOrdinalsTheEmitterActuallyBinds()
    {
        // Arrange -- two parameterized CTEs ahead of the boundary, so its ordinals are neither 0 nor
        // trivially aligned with anything: this is the drift the row exists to expose. Emit and Describe run
        // on the same plan and are cross-checked against each other rather than against a pinned string.
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 103, 202, new Predicate.Equal(new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"))),
                new CteDefinition.ParamSource(tokenTable, 103, 44, new Predicate.Equal(new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"))),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            new CteRef(2),
            Includes: [new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 10)],
            Shape: new ResultShape.IncludesPage(new IncludeBoundary(111, 5000)));

        // Act
        var emitted = SqlBuilder.Run(plan);
        var rows = PlanExplainer.Describe(plan);

        // Assert -- the ordinals the emitted resume predicate really seeks on, read back out of the SQL
        var seek = Regex.Match(emitted.Sql, @"\(T1 > (@p\d+) OR \(T1 = @p\d+ AND Sid1 > (@p\d+)\)\)");
        seek.Success.ShouldBeTrue(emitted.Sql);
        var typeParam = seek.Groups[1].Value;
        var sidParam = seek.Groups[2].Value;
        emitted.Parameters.ShouldContain(p => p.Name == typeParam && Equals(p.Value, (short)111));
        emitted.Parameters.ShouldContain(p => p.Name == sidParam && Equals(p.Value, 5000L));

        var boundaryRow = rows.Where(r => r.CanonicalLabel == "includeBoundary").ShouldHaveSingleItem();
        boundaryRow.Kind.ShouldBe(PlanRowKind.IncludeBoundary);
        boundaryRow.Label.ShouldBe("includeBoundary");
        boundaryRow.Body.ShouldBe($"IncludeBoundary(type={typeParam}, sid={sidParam})");

        // The row sits where the emitter binds it: after the CTE graph, before the stage rows.
        rows.Select(r => r.CanonicalLabel).ToList().IndexOf("includeBoundary")
            .ShouldBeLessThan(rows.Select(r => r.CanonicalLabel).ToList().IndexOf("inc0"));
    }

    [Fact]
    public void GivenAnIncludesOnlyPlanWithAResumeBoundaryAndSurrogateRangeAndHash_WhenExplained_ThenEveryRowNamesTheOrdinalsTheEmitterActuallyBinds()
    {
        // Arrange -- exactly the combination SqlBuilder's own RejectUnsupportedCombinations guard message
        // recommends: bound the match set with SurrogateRange (an $export shard window) and page the include
        // rows with ResultShape.IncludesPage.Resume. WriteMatchPageCte binds SurrogateRange and
        // SearchParameterHash INSIDE itself, before the resume boundary is bound after it returns -- a
        // regression that reorders includeBoundary back ahead of them would make every ordinal here wrong,
        // and previously did (the boundary row used to render right after the CTE graph, before this pair).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 10)],
            Shape: new ResultShape.IncludesPage(new IncludeBoundary(111, 5000)),
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(7000L), new SqlParameterRef(8000L)),
            SearchParameterHash: new SqlParameterRef("hashv"));

        // Act
        var emitted = SqlBuilder.Run(plan);
        var rows = PlanExplainer.Describe(plan);

        // Assert -- real bind order: cte0's own predicate, then SurrogateRange, then SearchParameterHash
        // (both inside WriteMatchPageCte's BuildMatchWhereClauses call), then the resume boundary.
        emitted.Parameters.Select(p => p.Value).ShouldBe(["Smith", 7000L, 8000L, "hashv", (short)111, 5000L]);

        var surrogateRow = rows.Single(r => r.CanonicalLabel == "surrogateRange");
        surrogateRow.Body.ShouldBe("SurrogateRange(start=@p1, end=@p2)");

        var hashRow = rows.Single(r => r.CanonicalLabel == "searchParameterHash");
        hashRow.Body.ShouldBe("SearchParameterHash(hash=@p3)");

        var boundaryRow = rows.Single(r => r.CanonicalLabel == "includeBoundary");
        boundaryRow.Body.ShouldBe("IncludeBoundary(type=@p4, sid=@p5)");

        // Ordering, not just individual correctness: surrogateRange and searchParameterHash must both
        // precede includeBoundary.
        var labels = rows.Select(r => r.CanonicalLabel).ToList();
        labels.IndexOf("surrogateRange").ShouldBeLessThan(labels.IndexOf("includeBoundary"));
        labels.IndexOf("searchParameterHash").ShouldBeLessThan(labels.IndexOf("includeBoundary"));
    }

    [Fact]
    public void GivenACompartmentSourcePlan_WhenExplained_ThenPrintsTheGroupedTypeListAndSearchParamId()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var predicate = new Predicate.And(
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"), new SqlParameterRef((short)103)),
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceId"), new SqlParameterRef("123")));
        var plan = new QueryPlan([new CteDefinition.CompartmentSource([104, 106], 77, predicate)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = CompartmentSource[104,106,77]  ReferenceResourceTypeId = @p0 AND ReferenceResourceId = @p1");
    }

    [Fact]
    public void GivenANotReferencedSourceWithSourceAndPath_WhenExplained_ThenPrintsTargetSourceAndParam()
    {
        // Arrange -- Patient?_not-referenced=Observation:subject.
        var plan = new QueryPlan([new CteDefinition.NotReferencedSource(103, 96, 969)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = NotReferencedSource[103] not referenced by source=96 ref=969");
    }

    [Fact]
    public void GivenANotReferencedSourceFullWildcard_WhenExplainedAlongsideAParameterizedCte_ThenConsumesOneOrdinalForItsTargetType()
    {
        // Arrange -- the target ResourceTypeId is a bound @pN in Emit, so Explain must consume an ordinal
        // for it too or the @pN numbering diverges from the emitted SQL when another parameterized CTE
        // follows. `*:*` prints without source/ref suffixes.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var plan = new QueryPlan(
        [
            new CteDefinition.NotReferencedSource(103, null, null),
            new CteDefinition.ParamSource(table, 103, 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith"))),
            new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
        ],
        new CteRef(2));

        // Act
        var explained = plan.Explain();

        // Assert -- the ParamSource's Text predicate is @p1, not @p0, because NotReferencedSource took @p0
        explained.ShouldBe(
            "cte0 = NotReferencedSource[103]\n" +
            "cte1 = StringSearchParam[103,202]  Text = @p1\n" +
            "root = Intersect(cte0, cte1)");
    }

    [Fact]
    public void GivenAPlanWithACustomSortAndATypelessPageBoundary_WhenExplained_ThenPrintsBothAsTrailingLinesAndConsumesNoTypeOrdinal()
    {
        // Arrange -- a custom sort requires a typeless boundary (SqlBuilder rejects the typed pairing), so
        // this is the shape a _sort=name continuation page actually has.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort, Page: page);

        // Act
        var explained = plan.Explain();

        // Assert -- "type=none", and the surrogate id takes @p2 rather than @p3: a typeless page binds no
        // type parameter, so printing one would misalign every later ordinal against what Emit binds.
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "sort = SortSpec([String:202 ASC], Valued)\n" +
            "page = PageSpec(boundary=[@p1], type=none, sid=@p2)");
    }

    [Fact]
    public void GivenAPlanWithAResourceColumnSortAndATypedPageBoundary_WhenExplained_ThenPrintsTheTypeParameterOrdinal()
    {
        // Arrange -- the other half of the enforced pairing: a resource-column sort keeps the m.T1 tiebreak,
        // so its boundary carries a type and the printed ordinals must account for it.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef(5000L)], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort, Page: page);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "sort = SortSpec([LastUpdated:- ASC], Valued)\n" +
            "page = PageSpec(boundary=[@p1], type=@p2, sid=@p3)");
    }

    [Fact]
    public void GivenACountOnlyPlan_WhenExplained_ThenPrintsCountOnlyLine()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Shape: new ResultShape.Count.AllMatches());

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "countOnly = true");
    }

    [Fact]
    public void GivenAnIsNullPredicate_WhenExplained_ThenPrintsColumnIsNull()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var predicate = new Predicate.IsNull(new SqlColumnRef(table.TableName, "SystemId"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 44, predicate)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = TokenSearchParam[103,44]  SystemId IS NULL");
    }

    [Fact]
    public void GivenAFalsePredicate_WhenExplained_ThenPrintsTheSameUnsatisfiableLiteralTheSqlEmitterUses()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var predicate = new Predicate.False();
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 44, predicate)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = TokenSearchParam[103,44]  1 = 0");
    }

    [Fact]
    public void GivenAPrefixOfParameterPredicate_WhenExplained_ThenPrintsPrefixOfWithCollation()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("UriSearchParam");
        var predicate = new Predicate.PrefixOfParameter(
            new SqlColumnRef(table.TableName, "Uri"),
            new SqlParameterRef("http://example.org/fhir/Patient/123"),
            "Latin1_General_100_BIN2");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = UriSearchParam[103,202]  Uri PREFIX_OF @p0 collate Latin1_General_100_BIN2");
    }

    [Fact]
    public void GivenAPrefixOfParameterPredicateWithoutCollation_WhenExplained_ThenPrintsPrefixOfWithoutCollation()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("UriSearchParam");
        var predicate = new Predicate.PrefixOfParameter(
            new SqlColumnRef(table.TableName, "Uri"),
            new SqlParameterRef("http://example.org/fhir/Patient/123"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = UriSearchParam[103,202]  Uri PREFIX_OF @p0");
    }

    [Fact]
    public void GivenAMultiTypeResourceSourceWithSeveralTypes_WhenExplained_ThenPrintsAllTypeIds()
    {
        // Arrange -- GET /?_type=Patient,Observation
        var plan = new QueryPlan([CteDefinition.MultiTypeResourceSource.ForTypes([103, 104])], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = MultiTypeResourceSource[103,104]");
    }

    [Fact]
    public void GivenAMultiTypeResourceSourceWithNoTypes_WhenExplained_ThenPrintsStarForSystemWide()
    {
        // Arrange -- bare GET / (no _type filter)
        var plan = new QueryPlan([CteDefinition.MultiTypeResourceSource.AllTypes()], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = MultiTypeResourceSource[*]");
    }

    [Fact]
    public void GivenAMultiTypeResourceSourceAlongsideAParameterizedCte_WhenExplained_ThenOrdinalNumberingIsUnaffected()
    {
        // MultiTypeResourceSource has no bound parameters (type ids are literals), so it must NOT
        // consume an ordinal. A ParamSource following it must still start at @p0.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var plan = new QueryPlan(
        [
            CteDefinition.MultiTypeResourceSource.ForTypes([103, 104]),
            new CteDefinition.ParamSource(table, 103, 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith"))),
            new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
        ],
        new CteRef(2));

        // Act
        var explained = plan.Explain();

        // Assert -- @p0 belongs to the ParamSource, not consumed by the MultiTypeResourceSource
        explained.ShouldBe(
            "cte0 = MultiTypeResourceSource[103,104]\n" +
            "cte1 = StringSearchParam[103,202]  Text = @p0\n" +
            "root = Intersect(cte0, cte1)");
    }

    [Fact]
    public void GivenASurrogateRangePlan_WhenExplained_ThenTheSurrogateRangeRowNamesTheSameOrdinalsEmitBinds()
    {
        // Arrange -- SurrogateRange is how $export shards its read window; BuildMatchWhereClauses binds it
        // right after the seek/outer predicate and before ORDER BY, so its ordinals land after cte0's own.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)));

        // Act
        var emitted = SqlBuilder.Run(plan);
        var explained = plan.Explain();

        // Assert
        emitted.Parameters.Select(p => p.Value).ShouldBe(["Smith", 5000L, 6000L]);
        explained.ShouldContain("surrogateRange = SurrogateRange(start=@p1, end=@p2)");
    }

    [Fact]
    public void GivenASearchParameterHashPlan_WhenExplained_ThenTheHashRowNamesTheSameOrdinalEmitBinds()
    {
        // Arrange -- SearchParameterHash gates reindex eligibility; its ordinal comes right after
        // SurrogateRange's in BuildMatchWhereClauses, so combining both proves the rows don't collide.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            SurrogateRange: new SurrogateIdRange(new SqlParameterRef(5000L), new SqlParameterRef(6000L)),
            SearchParameterHash: new SqlParameterRef("abc123"));

        // Act
        var emitted = SqlBuilder.Run(plan);
        var explained = plan.Explain();

        // Assert
        emitted.Parameters.Select(p => p.Value).ShouldBe(["Smith", 5000L, 6000L, "abc123"]);
        explained.ShouldContain("surrogateRange = SurrogateRange(start=@p1, end=@p2)");
        explained.ShouldContain("searchParameterHash = SearchParameterHash(hash=@p3)");
    }

    [Fact]
    public void GivenAnIncludesPlanWithSortAndAnOuterPredicate_WhenExplained_ThenTheMatchPageRowReportsBothJoins()
    {
        // Arrange -- sortJoins and resourceJoin are false in every other matchPage fixture in this file;
        // this one forces both true so PrintMatchPageCte's flags are proven on their true path too.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outer = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            OuterPredicate: outer,
            Includes: [new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 10)],
            Sort: new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued),
            Top: 25);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldContain("matchPage = MatchPageCte(top=25, sortJoins=true, resourceJoin=true)");
    }

    [Fact]
    public void GivenAProbeTrimmedIncludesPlanWithOffsetPage_WhenExplained_ThenTheMatchSeedRowReportsTheLimitNotTheFetchCountAndOrdinalsMatchEmit()
    {
        // Arrange -- ProbeExtraRow fetches Limit+1 rows so hasMore can be detected; cteMatchSeed trims that
        // probe row back off before include stages seed from it. The row must show Limit (10), not
        // FetchCount (11) -- reporting FetchCount here would misrepresent what include stages seed from.
        // Also cross-checks against the real emitted SQL: this is the one combination (ProbeExtraRow: true)
        // where matchSeed actually appears, and a prior version of this test only ever called plan.Explain(),
        // never SqlBuilder.Run -- so the ordinal claim was unverified for the one case it exists to cover.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000)],
            OffsetPage: new OffsetSpec(20, 10, ProbeExtraRow: true));

        // Act
        var emitted = SqlBuilder.Run(plan);
        var explained = plan.Explain();

        // Assert -- @p0 is cte0's own ResourceSource type id; @p1/@p2 are Offset/FetchCount (11 = Limit + probe row).
        emitted.Parameters.Select(p => p.Value).ShouldBe([(short)103, 20, 11]);
        explained.ShouldContain("matchSeed = MatchSeedCte(limit=10)");
        explained.ShouldContain("offsetPage = OffsetSpec(offset=@p1, fetch=@p2)");
    }

    [Fact]
    public void GivenAnIncludesPlanSortedOnlyByLastUpdated_WhenExplained_ThenTheMatchPageRowReportsNoSortJoin()
    {
        // Arrange -- EmitSortJoins skips LastUpdated/ResourceType keys entirely: the match set already
        // projects the surrogate id those sort on, no join needed. Before this test, PrintMatchPageCte
        // computed sortJoins as `plan.Sort is not null`, which is true here even though WriteMatchPageCte
        // emits zero JOIN clauses for this exact plan -- a real divergence, not just an unlikely one:
        // `_sort=_lastUpdated&_include=...` is an ordinary, common query shape.
        var plan = new QueryPlan(
            [new CteDefinition.ResourceSource(103)],
            new CteRef(0),
            Includes: [new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 10)],
            Sort: new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SortOrder.Descending)], SortPhase.Valued));

        // Act
        var emitted = SqlBuilder.Run(plan);
        var explained = plan.Explain();

        // Assert -- "sk0" is EmitSortJoins' own alias for a sort-key join; the include stage still joins
        // dbo.ReferenceSearchParam for its own reasons, so this checks for the absence of a sort join
        // specifically, not every join in the emitted SQL.
        emitted.Sql.ShouldNotContain("sk0");
        explained.ShouldContain("matchPage = MatchPageCte(top=none, sortJoins=false, resourceJoin=false)");
    }

    [Fact]
    public void GivenACountOnlyPlanWithPageAndOffsetPageSet_WhenExplained_ThenNeitherRowAppears()
    {
        // Arrange -- EmitCountOnlyShape reads neither Page nor OffsetPage (COUNT_BIG has no page to seek or
        // window), so a CountOnly plan that still carries either -- constructible directly against QueryPlan,
        // a public surface -- must not claim ordinals Emit never binds for them. Page and OffsetPage are
        // mutually exclusive with each other but each independently combinable with CountOnly, so two
        // separate CountOnly plans, not one plan carrying both.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var pagedCountPlan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Shape: new ResultShape.Count.AllMatches(),
            Page: new PageSpec([new SqlParameterRef("Adams")], BoundaryResourceTypeId: null, new SqlParameterRef(5000L)));
        var offsetCountPlan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Shape: new ResultShape.Count.AllMatches(),
            OffsetPage: new OffsetSpec(20, 10));

        // Act
        var pagedEmitted = SqlBuilder.Run(pagedCountPlan);
        var offsetEmitted = SqlBuilder.Run(offsetCountPlan);
        var pagedExplained = pagedCountPlan.Explain();
        var offsetExplained = offsetCountPlan.Explain();

        // Assert -- Emit binds only cte0's own predicate for both; Describe must claim exactly that, not more.
        pagedEmitted.Parameters.Select(p => p.Value).ShouldBe(["Smith"]);
        pagedExplained.ShouldNotContain("page = ");
        pagedExplained.ShouldContain("countOnly = true");

        offsetEmitted.Parameters.Select(p => p.Value).ShouldBe(["Smith"]);
        offsetExplained.ShouldNotContain("offsetPage = ");
        offsetExplained.ShouldContain("countOnly = true");
    }
}
