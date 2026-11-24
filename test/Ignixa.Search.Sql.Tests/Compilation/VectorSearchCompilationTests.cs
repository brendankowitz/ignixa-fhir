using System.Text.RegularExpressions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

/// <summary>
/// Semantic (vector) search lowering: the semantic leaf becomes a gating <see cref="CteDefinition.VectorMatchSource"/>
/// composed like any other leaf, and the match page ranks by the minimum chunk distance through a
/// <c>CROSS APPLY</c> described by <see cref="MatchPageSpec.Ranking"/>.
/// </summary>
public class VectorSearchCompilationTests
{
    private const short ObservationTypeId = 104;
    private const short PatientTypeId = 103;
    private const short SemanticParamId = 300;
    private const short StatusParamId = 220;
    private const short DateParamId = 230;
    private const short SubjectParamId = 240;
    private const short EmbeddingModelId = 7;
    private const string ModelKey = "text-embedding-3-small|1";

    private static readonly SearchParameterInfo SemanticParam = new(
        "semantic-text",
        "semantic-text",
        SearchParamType.Special,
        new Uri("http://example.org/fhir/SearchParameter/Observation-semantic-text"),
        vectorConfig: new VectorSearchConfig(VectorTextExtractionPolicy.Concatenate, 8000, 0m, null, null));

    private static readonly SearchParameterInfo StatusParam = new(
        "status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));

    private static readonly SearchParameterInfo DateParam = new(
        "date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/clinical-date"));

    private static readonly SearchParameterInfo SubjectParam = new(
        "subject",
        "subject",
        SearchParamType.Reference,
        new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
        targetResourceTypes: ["Patient"]);

    private static readonly SearchPlanOptions OffsetPage = new()
    {
        Shape = new ResultShape.Matches(new SearchPaging.Offset(new OffsetSpec(0, 10))),
    };

    [Fact]
    public async Task GivenSemanticOnly_WhenCompiled_ThenGateCteAndDistanceOrderBy()
    {
        var plan = await PlanAsync(Options(Semantic(seed: 0.1f)), OffsetPage);

        var gate = plan.Query.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.VectorMatchSource>();
        gate.ResourceTypeId.ShouldBe(ObservationTypeId);
        gate.SearchParamId.ShouldBe(SemanticParamId);
        gate.EmbeddingModelId.ShouldBe(EmbeddingModelId);
        plan.Query.MatchSpec.Ranking.ShouldNotBeNull().Source.ShouldBeSameAs(gate);

        var sql = plan.Compile().Sql;
        sql.ShouldContain(
            "    SELECT DISTINCT v.ResourceTypeId AS T1, v.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.VectorSearchParam v\n" +
            "    INNER JOIN dbo.Resource r ON r.ResourceTypeId = v.ResourceTypeId AND r.ResourceSurrogateId = v.ResourceSurrogateId AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            $"    WHERE v.ResourceTypeId = {ObservationTypeId} AND v.SearchParamId = {SemanticParamId} AND v.EmbeddingModelId = {EmbeddingModelId} " +
            "AND VECTOR_DISTANCE('cosine', v.Embedding, CAST(@p0 AS vector(1536))) <= @p1");
        sql.ShouldContain(
            "SELECT m.T1, m.Sid1, vr.Distance AS Distance FROM cte0 m\n" +
            "CROSS APPLY (SELECT MIN(VECTOR_DISTANCE('cosine', vsp.Embedding, CAST(@p2 AS vector(1536)))) AS Distance " +
            "FROM dbo.VectorSearchParam vsp WHERE vsp.ResourceTypeId = m.T1 AND vsp.ResourceSurrogateId = m.Sid1 " +
            $"AND vsp.SearchParamId = {SemanticParamId} AND vsp.EmbeddingModelId = {EmbeddingModelId}) vr");
        OrderBy(sql).ShouldBe("vr.Distance ASC, m.T1 ASC, m.Sid1 ASC");

        var explain = plan.Query.Explain();
        explain.ShouldContain($"root = VectorMatchSource[{ObservationTypeId},{SemanticParamId},model={EmbeddingModelId}]  VECTOR_DISTANCE(Embedding, @p0) <= @p1");
        explain.ShouldContain("ranking = VectorRank(source=cte0, embedding=@p2)");
    }

    [Fact]
    public async Task GivenSemanticAndStatus_WhenCompiled_ThenGateIntersectedWithTokenCte()
    {
        var expression = Expression.And(Semantic(seed: 0.1f), StatusEquals("final"));

        var plan = await PlanAsync(Options(expression), OffsetPage);

        plan.Query.Ctes[0].ShouldBeOfType<CteDefinition.VectorMatchSource>();
        plan.Query.Ctes[1].ShouldBeOfType<CteDefinition.ParamSource>().Table.TableName.ShouldBe("TokenSearchParam");
        var root = plan.Query.Ctes[plan.Query.Match.Index].ShouldBeOfType<CteDefinition.Intersect>();
        (root.Left.Index, root.Right.Index).ShouldBe((0, 1));
        plan.Query.MatchSpec.Ranking.ShouldNotBeNull().Source.ShouldBeSameAs(plan.Query.Ctes[0]);

        var compiled = plan.Compile();
        compiled.Sql.ShouldContain("FROM cte2 m\nCROSS APPLY (");
        compiled.Parameters.Select(p => p.Value).ShouldContain("final");
        plan.Query.Explain().ShouldContain("root = Intersect(cte0, cte1)");
    }

    [Fact]
    public async Task GivenSemanticAndSortDate_WhenCompiled_ThenDateSortThenDistanceThenIdentity()
    {
        var options = Options(Semantic(seed: 0.1f));
        options.Sort = [new SortExpression(DateParam, SortOrder.Descending)];

        var plan = await PlanAsync(options, OffsetPage);

        // A date sort is a custom key, so the identity tie-break is Sid1 alone; distance sits between them.
        var orderBy = OrderBy(plan.Compile().Sql);
        orderBy.ShouldEndWith(" DESC, vr.Distance ASC, m.Sid1 ASC");
        orderBy.ShouldNotStartWith("vr.Distance");
    }

    [Fact]
    public async Task GivenSemanticWithCountOnly_WhenCompiled_ThenNoCrossApply()
    {
        var plan = await PlanAsync(
            Options(Semantic(seed: 0.1f)),
            new SearchPlanOptions { Shape = new ResultShape.Count.AllMatches() });

        plan.Query.MatchSpec.Ranking.ShouldBeNull();
        plan.Query.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.VectorMatchSource>();

        var compiled = plan.Compile();
        compiled.Sql.ShouldNotContain("CROSS APPLY");
        compiled.Sql.ShouldNotContain("AS Distance", Case.Sensitive);
        Regex.Matches(compiled.Sql, "VECTOR_DISTANCE").Count.ShouldBe(1);
        compiled.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m");
    }

    [Fact]
    public async Task GivenSemanticWithAccessConstraint_WhenCompiled_ThenConstraintAppliedBeforeRanking()
    {
        var options = Options(Semantic(seed: 0.1f));
        options.AccessConstraints = [new AccessConstraint("Observation", StatusEquals("amended"))];

        var plan = await PlanAsync(options, OffsetPage);

        // The ranked set is the constrained match root, not the bare gate: ranking can only reorder rows the
        // constraint already admitted.
        var rootIndex = plan.Query.Match.Index;
        var root = plan.Query.Ctes[rootIndex].ShouldBeOfType<CteDefinition.Intersect>();
        new[] { root.Left.Index, root.Right.Index }.ShouldContain(0);
        plan.Query.MatchSpec.Ranking.ShouldNotBeNull().Source.ShouldBeSameAs(plan.Query.Ctes[0]);

        var compiled = plan.Compile();
        compiled.Parameters.Select(p => p.Value).ShouldContain("amended");
        compiled.Sql.ShouldContain($"FROM {SqlLabels.CteLabel(rootIndex)} m\nCROSS APPLY (");
    }

    [Fact]
    public async Task GivenUnpreparedSemantic_WhenCompiled_ThenSearchCompilationException()
    {
        var compiler = new SearchSqlCompiler(Resolver());
        var options = Options(new VectorSearchExpression(SemanticParam, "chest pain"));

        var exception = await Should.ThrowAsync<SearchCompilationException>(
            () => compiler.CreatePlanFromOptionsAsync(options, "Observation", OffsetPage));

        exception.Failure.Stage.ShouldBe(CompilationStage.Lower);
        exception.Message.ShouldContain("Semantic query was not prepared");
    }

    [Fact]
    public async Task GivenSemanticWithKeysetPaging_WhenValidated_ThenRejected()
    {
        var plan = await PlanAsync(Options(Semantic(seed: 0.1f)), OffsetPage);
        var keysetSpec = plan.Query.MatchSpec with
        {
            OffsetPage = null,
            Top = 11,
            Page = new PageSpec([], new SqlParameterRef(ObservationTypeId), new SqlParameterRef(5000L)),
        };
        var rewritten = plan with { Query = plan.Query with { MatchSpec = keysetSpec } };

        var result = rewritten.TryCompile();

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Emit);
        result.Failure.Message.ShouldContain("keyset");
    }

    [Fact]
    public async Task GivenSemanticWithKeysetPagingOption_WhenCompiled_ThenRejectedAtLower()
    {
        var compiler = new SearchSqlCompiler(Resolver());
        var options = new SearchPlanOptions { Shape = new ResultShape.Matches(new SearchPaging.Keyset(Top: 11, TopIncludesProbeRow: true)) };

        var exception = await Should.ThrowAsync<SearchCompilationException>(
            () => compiler.CreatePlanFromOptionsAsync(Options(Semantic(seed: 0.1f)), "Observation", options));

        exception.Failure.Stage.ShouldBe(CompilationStage.Lower);
        exception.Message.ShouldContain("keyset");
    }

    [Fact]
    public async Task GivenTwoQueriesDifferentEmbeddings_WhenCompiled_ThenSameSqlTextDifferentParameters()
    {
        var first = (await PlanAsync(Options(Semantic(seed: 0.1f, maxDistance: 2.0)), OffsetPage)).Compile();
        var second = (await PlanAsync(Options(Semantic(seed: 0.9f, maxDistance: 0.4)), OffsetPage)).Compile();

        first.Sql.ShouldBe(second.Sql);

        var firstEmbedding = first.Parameters[0].Value.ShouldBeOfType<string>();
        var secondEmbedding = second.Parameters[0].Value.ShouldBeOfType<string>();
        firstEmbedding.ShouldNotBe(secondEmbedding);
        firstEmbedding.ShouldStartWith("[");
        first.Sql.ShouldNotContain(firstEmbedding[..20]);
        first.Parameters[1].Value.ShouldBe(2.0);
        second.Parameters[1].Value.ShouldBe(0.4);

        // The rank binds the same embedding the gate does.
        first.Parameters[2].Value.ShouldBe(firstEmbedding);
    }

    [Fact]
    public async Task GivenSemanticUnderOr_WhenCompiled_ThenRejected()
    {
        var compiler = new SearchSqlCompiler(Resolver());
        var options = Options(Expression.Or(Semantic(seed: 0.1f), StatusEquals("final")));

        var exception = await Should.ThrowAsync<SearchCompilationException>(
            () => compiler.CreatePlanFromOptionsAsync(options, "Observation", OffsetPage));

        exception.Failure.Stage.ShouldBe(CompilationStage.Lower);
        exception.Message.ShouldContain("top-level");
    }

    [Fact]
    public async Task GivenSemanticUnderNot_WhenCompiled_ThenRejected()
    {
        var compiler = new SearchSqlCompiler(Resolver());
        var negated = new SearchParameterExpression(SemanticParam, Expression.Not(Semantic(seed: 0.1f)));
        var options = Options(Expression.And(StatusEquals("final"), negated));

        var exception = await Should.ThrowAsync<SearchCompilationException>(
            () => compiler.CreatePlanFromOptionsAsync(options, "Observation", OffsetPage));

        exception.Message.ShouldContain("top-level");
    }

    [Fact]
    public async Task GivenTwoSemanticExpressions_WhenCompiled_ThenRejected()
    {
        var compiler = new SearchSqlCompiler(Resolver());
        var options = Options(Expression.And(Semantic(seed: 0.1f), Semantic(seed: 0.2f)));

        var exception = await Should.ThrowAsync<SearchCompilationException>(
            () => compiler.CreatePlanFromOptionsAsync(options, "Observation", OffsetPage));

        exception.Message.ShouldContain("Only one semantic search parameter");
    }

    [Fact]
    public async Task GivenUnknownEmbeddingModel_WhenCompiled_ThenMatchesNothing()
    {
        var resolver = Resolver();
        resolver.EmbeddingModelIds.Clear();

        var plan = await PlanAsync(Options(Semantic(seed: 0.1f)), OffsetPage, resolver);

        // No dbo.EmbeddingModel row means no vector was ever written under this model, so nothing can match:
        // the leaf lowers to the compiler's known-miss source and there is nothing to rank.
        var source = plan.Query.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ResourceSource>();
        source.Predicate.ShouldBeOfType<Predicate.False>().Reason.ShouldNotBeNull().ShouldContain(ModelKey);
        plan.Query.MatchSpec.Ranking.ShouldBeNull();

        var sql = plan.Compile().Sql;
        sql.ShouldContain("1 = 0");
        sql.ShouldNotContain("VECTOR_DISTANCE");
    }

    [Fact]
    public async Task GivenSemanticWithIncludeAndProbeRow_WhenCompiled_ThenIncludesSeedFromRankedPage()
    {
        var options = Options(Semantic(seed: 0.1f));
        options.Include = [new IncludeExpression(["Observation"], SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: false, iterate: false)];
        var planOptions = new SearchPlanOptions
        {
            IncludeLimit = 100,
            Shape = new ResultShape.Matches(new SearchPaging.Offset(new OffsetSpec(0, 10, ProbeExtraRow: true))),
        };

        var plan = await PlanAsync(options, planOptions);
        var sql = plan.Compile().Sql;

        // The page is ranked before OFFSET/FETCH draws it, so the probe row is the 11th-closest match...
        sql.ShouldContain(
            "    SELECT m.T1, m.Sid1, vr.Distance AS Distance\n" +
            "    FROM cte0 m\n" +
            "CROSS APPLY (");
        sql.ShouldContain("    ORDER BY vr.Distance ASC, m.T1 ASC, m.Sid1 ASC\n    OFFSET @p3 ROWS FETCH NEXT @p4 ROWS ONLY");

        // ...and the seed trims it under the same distance order, so includes never resolve for it.
        sql.ShouldContain(
            $"{SqlLabels.MatchSeed} AS (\n" +
            "    SELECT TOP (10) T1, Sid1\n" +
            $"    FROM {SqlLabels.MatchPage}\n" +
            "    ORDER BY Distance ASC, T1 ASC, Sid1 ASC\n)");
        sql.ShouldContain($"SELECT 1 FROM {SqlLabels.MatchSeed} m WHERE");

        // Matches carry their distance through the union; include rows carry none.
        sql.ShouldContain($"SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, Distance FROM {SqlLabels.MatchPage}");
        sql.ShouldContain("SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial, NULL FROM inc0lim i");
        OrderBy(sql).ShouldBe("IsMatch DESC, Distance ASC, T1 ASC, Sid1 ASC");

        plan.Query.Explain().ShouldContain("matchSeed = MatchSeedCte(limit=10)");
    }

    /// <summary>The text of the statement's final ORDER BY clause.</summary>
    private static string OrderBy(string sql)
    {
        var start = sql.LastIndexOf("\nORDER BY ", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var clause = sql[(start + "\nORDER BY ".Length)..];
        var end = clause.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? clause : clause[..end];
    }

    private static async Task<SearchPlan> PlanAsync(SearchOptions options, SearchPlanOptions planOptions, FakeSymbolResolver? resolver = null)
    {
        var compiler = new SearchSqlCompiler(resolver ?? Resolver());
        var result = await compiler.TryCreatePlanFromOptionsAsync(options, options.ResourceType, planOptions);
        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        return result.Plan!;
    }

    private static SearchOptions Options(Expression expression)
        => new() { ResourceType = "Observation", Expression = expression };

    private static FakeSymbolResolver Resolver()
    {
        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Observation"] = ObservationTypeId;
        resolver.ResourceTypeIds["Patient"] = PatientTypeId;
        resolver.SearchParamIds[SemanticParam.Url!.ToString()] = SemanticParamId;
        resolver.SearchParamIds[StatusParam.Url!.ToString()] = StatusParamId;
        resolver.SearchParamIds[DateParam.Url!.ToString()] = DateParamId;
        resolver.SearchParamIds[SubjectParam.Url!.ToString()] = SubjectParamId;
        resolver.EmbeddingModelIds[ModelKey] = EmbeddingModelId;
        return resolver;
    }

    private static VectorSearchExpression Semantic(float seed, double maxDistance = 1.2)
        => new VectorSearchExpression(SemanticParam, "chest pain, nausea")
            .WithPrepared(new PreparedVectorQuery(Embedding(seed), ModelKey, maxDistance));

    private static SearchParameterExpression StatusEquals(string code)
        => new(
            StatusParam,
            new SearchParameterPredicateExpression(StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null)));

    private static float[] Embedding(float seed)
        => Enumerable.Range(0, 1536).Select(i => seed + (i * 0.0001f)).ToArray();
}
