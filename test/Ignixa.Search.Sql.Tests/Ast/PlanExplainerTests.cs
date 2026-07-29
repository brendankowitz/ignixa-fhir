using Ignixa.Search.Expressions;
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
            "inc0 = IncludeStage(ref=*, seedTypes=*, outputTypes=*, seeds=[match], limit=500 iterate, Reverse)");
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
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), CountOnly: true);

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
}
