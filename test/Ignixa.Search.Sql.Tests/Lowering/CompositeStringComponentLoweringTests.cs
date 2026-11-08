// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tests.Corpus;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Regression: a composite String component whose value held a <c>|</c> or looked like <c>Type/id</c> was retyped by the
/// binder, so SQL looked for a token-token or token-reference composite and either failed to lower or matched nothing.
/// Compiled from the query string through the real R4 definitions.
/// </summary>
public class CompositeStringComponentLoweringTests
{
    private static readonly R4CoreSchemaProvider Schema = new();

    private static readonly SearchParameterDefinitionManager Definitions = new(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);

    [Theory]
    [InlineData("code-value-string=x$a|b", "a|b")]
    [InlineData("code-value-string=x$Patient/1", "Patient/1")]
    public async Task GivenAStringComponentValueShapedLikeATokenOrReference_WhenCompiled_ThenItLowersToTheTokenStringTableWithTheText(string query, string text)
    {
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => Definitions, new SearchParameterExpressionParser(new ReferenceSearchValueParser(Schema, NullFhirBaseUriProvider.Instance), Schema), Schema),
            Definitions);
        var compiler = new SearchSqlCompiler(new CorpusSymbolResolver(), builder, searchParameterDefinitionManager: Definitions);

        SearchPlanResult result = await compiler.TryCreatePlanAsync("Observation", new QueryParameterParser().Parse(query), new SearchPlanOptions());

        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        result.Plan!.Query.Explain().ShouldContain("TokenStringCompositeSearchParam");
        SearchCompilationResult compiled = result.Plan.TryCompile();
        compiled.Succeeded.ShouldBeTrue(compiled.Failure?.Message);
        // A string component is a prefix match, so the text is the LIKE pattern's stem.
        compiled.Compiled!.Parameters.Select(parameter => parameter.Value).ShouldContain($"{text}%");
    }
}
