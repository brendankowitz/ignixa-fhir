using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class LastNCompilationTests
{
    private static readonly SearchParameterInfo CodeParameter = new(
        "code",
        "code",
        SearchParamType.Token,
        new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));

    private static readonly SearchParameterInfo DateParameter = new(
        "date",
        "date",
        SearchParamType.Date,
        new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));

    private static readonly SearchParameterInfo StatusParameter = new(
        "status",
        "status",
        SearchParamType.Token,
        new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));

    [Fact]
    public async Task GivenDefaultLastNOptions_WhenCreatingAPlan_ThenResolvesTheTerminalOperationShape()
    {
        // Arrange
        var resolver = LastNResolver();
        var options = new LastNSearchOptions(new SearchOptions(), 1, CodeParameter, DateParameter);

        // Act
        SearchPlanResult result = await new SearchSqlCompiler(resolver).TryCreateLastNPlanAsync(options);

        // Assert
        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        ResultShape.LastN shape = result.Plan!.Query.EffectiveShape.ShouldBeOfType<ResultShape.LastN>();
        shape.Spec.ShouldBe(new LastNSpec(104, 210, 211, 1));

        CompiledSearch compiled = result.Plan.Compile();
        compiled.Parameters.ShouldBe(
        [
            new EmittedSqlParameter("@p0", (short)104),
            new EmittedSqlParameter("@p1", 1),
        ]);
        Ast.SqlGrammar.AssertValid(compiled.Sql);
    }

    [Fact]
    public async Task GivenAFilteredLastNRequest_WhenCompiled_ThenEmitsExactGroupingAndTieInclusiveRankingAfterTheCandidateSet()
    {
        // Arrange
        var resolver = LastNResolver();
        resolver.SearchParamIds[StatusParameter.Url!.ToString()] = 212;
        var statusLeaf = new SearchParameterPredicateExpression(
            StatusParameter,
            SearchComparator.Eq,
            modifier: null,
            new TokenSearchValue(system: null, code: "final", text: null));
        var filters = new SearchOptions
        {
            Expression = new SearchParameterExpression(StatusParameter, statusLeaf),
        };
        var options = new LastNSearchOptions(filters, 3, CodeParameter, DateParameter);

        // Act
        SearchPlan plan = await new SearchSqlCompiler(resolver).CreateLastNPlanAsync(
            options,
            new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Full });
        CompiledSearch compiled = plan.Compile();

        // Assert
        compiled.Sql.ShouldContain("FROM cte0 m");
        compiled.Sql.ShouldContain("SearchParamId = 212");
        compiled.Sql.ShouldContain("AND candidate.T1 = 104");
        compiled.Sql.ShouldContain("codeRow.SystemId, CONCAT(codeRow.Code, codeRow.CodeOverflow)) AS NodeId");
        compiled.Sql.ShouldContain("INTO #code_edges");
        compiled.Sql.ShouldContain("membership.NodeId AS ComponentId");
        compiled.Sql.ShouldContain("SET ComponentId = neighbors.ComponentId");
        compiled.Sql.ShouldNotContain("#code_reach");
        compiled.Sql.ShouldContain("textRow.Text COLLATE Latin1_General_100_CS_AS");
        compiled.Sql.ShouldContain("dateRow.IsMax = 1");
        compiled.Sql.ShouldContain(
            "RANK() OVER (\n" +
            "            PARTITION BY GroupKind, CodeGroupId, TextCode\n" +
            "            ORDER BY CASE WHEN EffectiveStart IS NULL THEN 1 ELSE 0 END,\n" +
            "                     EffectiveStart DESC,\n" +
            "                     CASE WHEN EffectiveStart IS NULL THEN Sid1 END DESC)");
        compiled.Sql.ShouldContain("WHERE EffectiveRank <= @p1");
        compiled.Parameters.Select(parameter => parameter.Value).ShouldBe(["final", 3]);
        plan.Query.Explain().ShouldContain("lastN = LastNSpec(type=104, code=210, date=211, max=@p1)");
        Ast.SqlGrammar.AssertValid(compiled.Sql);
    }

    [Fact]
    public void GivenACyclicCodeGraph_WhenCompiled_ThenClosureStoresOneComponentLabelPerCodeNode()
    {
        // Arrange
        var plan = new SearchPlan
        {
            Query = new QueryPlan(
                [new CteDefinition.ResourceSource(104)],
                new MatchPageSpec(
                    new CteRef(0),
                    Shape: new ResultShape.LastN(new LastNSpec(104, 210, 211, 1)))),
        };

        // Act
        string sql = plan.Compile().Sql;

        // Assert
        sql.ShouldContain("membership.NodeId AS ComponentId");
        sql.ShouldContain("SET ComponentId = neighbors.ComponentId");
        sql.ShouldContain("WHERE neighbors.ComponentId < target.ComponentId");
        sql.ShouldContain("toCode.NodeId AS ToNodeId");
        sql.ShouldContain("node.NodeId = membership.NodeId");
        sql.ShouldNotContain("fromNode.SystemId");
        sql.ShouldContain("WHILE");
        sql.ShouldNotContain("#code_reach");
        sql.ShouldNotContain("RootNodeId");
        sql.ShouldNotContain("OPTION (MAXRECURSION 0)");
        sql.ShouldContain("DROP TABLE #code_edges, #code_nodes, #coded_membership, #lastn_candidates");
        Ast.SqlGrammar.AssertValid(sql);
    }

    [Fact]
    public void GivenHistoryOnlyVisibility_WhenCompiled_ThenTextGroupingUsesResourceIdentityWithoutATokenTextHistoryPredicate()
    {
        // Arrange
        var plan = new SearchPlan
        {
            Query = new QueryPlan(
                [new CteDefinition.ResourceSource(104)],
                new MatchPageSpec(
                    new CteRef(0),
                    Shape: new ResultShape.LastN(new LastNSpec(104, 210, 211, 1))),
                Visibility: new ResourceVisibility(IsHistory: true, IsDeleted: false)),
        };

        // Act
        string sql = plan.Compile().Sql;

        // Assert
        sql.ShouldContain("IsHistory = 1");
        sql.ShouldNotContain("textRow.IsHistory");
        Ast.SqlGrammar.AssertValid(sql);
    }

    [Theory]
    [InlineData("sort")]
    [InlineData("count")]
    [InlineData("continuation")]
    [InlineData("include")]
    [InlineData("revinclude")]
    public async Task GivenAnOrdinaryResultControl_WhenCompilingLastN_ThenFailsLoudly(string control)
    {
        // Arrange
        SearchOptions filters = new();
        bool countSpecified = false;
        switch (control)
        {
            case "sort":
                filters.Sort = [new SortExpression(DateParameter, SortOrder.Descending)];
                break;
            case "count":
                countSpecified = true;
                break;
            case "continuation":
                filters.ContinuationToken = "next-page";
                break;
            case "include":
                filters.Include = [IncludeExpression()];
                break;
            case "revinclude":
                filters.RevInclude = [IncludeExpression(reversed: true)];
                break;
        }

        var options = new LastNSearchOptions(filters, 1, CodeParameter, DateParameter, countSpecified);

        // Act
        SearchPlanResult result = await new SearchSqlCompiler(LastNResolver()).TryCreateLastNPlanAsync(options);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Lower);
        result.Failure.Message.ShouldContain(control is "continuation" ? "continuation" : $"_{control}");
    }

    [Fact]
    public async Task GivenOrdinaryPagingInSearchPlanOptions_WhenCompilingLastN_ThenRejectsRatherThanIgnoringIt()
    {
        var options = new LastNSearchOptions(new SearchOptions(), 1, CodeParameter, DateParameter);
        var planOptions = new SearchPlanOptions
        {
            Shape = new ResultShape.Matches(new SearchPaging.Keyset(Top: 10)),
        };

        SearchPlanResult result = await new SearchSqlCompiler(LastNResolver())
            .TryCreateLastNPlanAsync(options, planOptions);

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Lower);
        result.Failure.Message.ShouldContain("paging");
    }

    [Fact]
    public async Task GivenWrongImplicitParameterTypes_WhenCompilingLastN_ThenRejectsAtTheLowerInputBoundary()
    {
        var wrongCode = new SearchParameterInfo(
            "code",
            "code",
            SearchParamType.String,
            CodeParameter.Url);
        var options = new LastNSearchOptions(new SearchOptions(), 1, wrongCode, DateParameter);

        SearchPlanResult result = await new SearchSqlCompiler(LastNResolver())
            .TryCreateLastNPlanAsync(options);

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Lower);
        result.Failure.Message.ShouldContain("Token");
    }

    [Fact]
    public async Task GivenTheDefaultLastNPlan_WhenCompiled_ThenMatchesTheApprovedSqlGolden()
    {
        var options = new LastNSearchOptions(new SearchOptions(), 1, CodeParameter, DateParameter);
        SearchPlan plan = await new SearchSqlCompiler(LastNResolver()).CreateLastNPlanAsync(options);

        string sql = plan.Compile().Sql;
        string golden = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sql)));

        golden.ShouldBe("9778EC2D58FF08E7AED6A7C02DFEDFAE6D23F9E4EA1B47FE0A60EA65E3854146");
    }

    [Fact]
    public async Task GivenMalformedCandidateOptions_WhenTryingToCreateLastNPlan_ThenReturnsABuildFailure()
    {
        var filters = new SearchOptions { ResourceVersionTypes = ResourceVersionTypes.None };
        var options = new LastNSearchOptions(filters, 1, CodeParameter, DateParameter);

        SearchPlanResult result = await new SearchSqlCompiler(LastNResolver())
            .TryCreateLastNPlanAsync(
                options,
                new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Parameters });

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Build);
        result.Failure.Diagnostics.ShouldNotBeNull();
    }

    private static IncludeExpression IncludeExpression(bool reversed = false)
    {
        var subject = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        return new IncludeExpression(
            ["Observation"],
            subject,
            "Observation",
            "Patient",
            referencedTypes: null,
            wildCard: false,
            reversed,
            iterate: false);
    }

    private static FakeSymbolResolver LastNResolver()
    {
        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.SearchParamIds[CodeParameter.Url!.ToString()] = 210;
        resolver.SearchParamIds[DateParameter.Url!.ToString()] = 211;
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-subject"] = 213;
        resolver.ResourceTypeIds["Patient"] = 103;
        return resolver;
    }
}
