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
/// Regression: SQL lowered the two components of a reference-token composite by value type, so a query giving them in the
/// opposite order to the definition matched in SQL while the in-memory matcher (correctly, composites being positional)
/// returned nothing. Compiled from the query string through the real R4 definitions, where
/// <c>DocumentReference-relationship</c> is repaired to declare <c>relatesto</c> (reference) then <c>relation</c> (token).
/// The declared-order test is a guard (mutation-checked); the opposite-order rows fail at 206143a.
/// </summary>
public class ReferenceTokenCompositeOrderTests
{
    private static readonly R4CoreSchemaProvider Schema = new();

    private static readonly SearchParameterDefinitionManager Definitions = new(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);

    [Fact]
    public async Task GivenRelationshipValuesInTheDeclaredOrder_WhenCompiled_ThenTheReferenceAndTokenSlotsAreCompared()
    {
        string explain = await ExplainAsync("relationship=DocumentReference/dr0$http://hl7.org/fhir/document-relationship-type|replaces");

        explain.ShouldContain("ReferenceTokenCompositeSearchParam");
        explain.ShouldContain("ReferenceResourceId1 = @p");
        explain.ShouldContain("Code2 = @p");
        explain.ShouldNotContain("1 = 0");
    }

    [Theory]
    [InlineData("relationship=http://hl7.org/fhir/document-relationship-type|replaces$DocumentReference/dr0")]
    [InlineData("relationship=|replaces$DocumentReference/dr0")]
    public async Task GivenRelationshipValuesInTheOppositeOrder_WhenCompiled_ThenNothingCanMatch(string query)
    {
        string explain = await ExplainAsync(query);

        explain.ShouldContain("ReferenceTokenCompositeSearchParam");
        explain.ShouldContain("1 = 0");
        explain.ShouldNotContain("ReferenceResourceId1 = @p");
    }

    private static async Task<string> ExplainAsync(string query)
    {
        var builder = new SearchOptionsBuilder(
            new ExpressionParser(() => Definitions, new SearchParameterExpressionParser(new ReferenceSearchValueParser(Schema, NullFhirBaseUriProvider.Instance), Schema), Schema),
            Definitions);
        var compiler = new SearchSqlCompiler(new CorpusSymbolResolver(), builder, searchParameterDefinitionManager: Definitions);

        SearchPlanResult result = await compiler.TryCreatePlanAsync("DocumentReference", new QueryParameterParser().Parse(query), new SearchPlanOptions());

        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        return result.Plan!.Query.Explain();
    }
}
