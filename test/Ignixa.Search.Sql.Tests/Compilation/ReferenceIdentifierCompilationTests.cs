using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Tests.Corpus;
using Ignixa.Serialization.Abstractions;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class ReferenceIdentifierCompilationTests
{
    private static readonly R4CoreSchemaProvider Schema = new();
    private static readonly QueryParameterParser QueryParser = new();
    private static readonly SearchParameterDefinitionManager Definitions =
        new(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);
    private static readonly SearchOptionsBuilder OptionsBuilder = new(
        new ExpressionParser(
            () => Definitions,
            new SearchParameterExpressionParser(new ReferenceSearchValueParser(Schema, NullFhirBaseUriProvider.Instance), Schema),
            Schema),
        Definitions);

    [Fact]
    public async Task GivenReferenceIdentifierQuery_WhenCompiled_ThenEmitsSystemAndCodeTokenPredicates()
    {
        // Act
        SearchPlanResult result = await CompileAsync("patient:identifier=http://example.org/facilityA|1234");
        QueryPlan plan = result.Plan!.Query;

        // Assert
        plan.Explain().ShouldBe("root = TokenSearchParam[1,1000]  SystemId = @p0 AND Code = @p1");
        var emitted = result.Plan.Compile();
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 1 AND SearchParamId = 1000 AND (SystemId = @p0 AND Code = @p1)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Select(parameter => parameter.Name).ShouldBe(["@p0", "@p1"]);
        emitted.Parameters.Select(parameter => parameter.Value).ShouldBe([1, "1234"]);
    }

    private static async Task<SearchPlanResult> CompileAsync(string queryString)
    {
        var compiler = new SearchSqlCompiler(new CorpusSymbolResolver(), OptionsBuilder, searchParameterDefinitionManager: Definitions);
        SearchPlanResult result = await compiler.TryCreatePlanAsync(
            "Encounter",
            QueryParser.Parse(queryString),
            new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.None },
            CancellationToken.None);

        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        return result;
    }
}
