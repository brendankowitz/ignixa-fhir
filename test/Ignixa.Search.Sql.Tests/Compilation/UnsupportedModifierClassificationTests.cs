using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.Corpus;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Serialization.Abstractions;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

/// <summary>
/// Pins that a modifier the server does not support for a parameter is recorded in
/// <see cref="SearchOptions.UnsupportedModifierParams"/>, not merely dropped into the same bucket as an
/// unknown parameter.
/// <para>
/// The two look identical downstream — both leave <see cref="SearchOptions.Expression"/> without the
/// parameter, so <c>Observation?_id:above=abc</c> compiles to a bare ResourceSource with no outer
/// predicate and matches every Observation. FHIR R4 does not treat them the same: an unknown or
/// unsupported parameter SHOULD be ignored, while a request "suffixed by a modifier that the server does
/// not support for that parameter" SHALL be rejected with a 400. Dropping the former narrows nothing;
/// dropping the latter widens the result set to everything, which a client reading the entry list cannot
/// distinguish from a filter that matched everything.
/// </para>
/// <para>
/// The classification is asserted here, on real R4 definitions through the real
/// <see cref="SearchOptionsBuilder"/>, because that is where the two cases are told apart. Whether the
/// classification then produces a 400 is the HTTP boundary's decision and is pinned separately.
/// </para>
/// </summary>
public class UnsupportedModifierClassificationTests
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

    [Theory]
    [InlineData("_id:above=abc")]
    [InlineData("_id:exact=abc")]
    [InlineData("_lastUpdated:above=2020")]
    public void GivenAnUnsupportedModifierOnAnIntrinsicParameter_WhenBuilt_ThenItIsRecordedAsAnUnsupportedModifier(string queryString)
    {
        // Arrange
        var expectedKey = queryString.Split('=')[0];

        // Act
        var options = OptionsBuilder.Build("Observation", QueryParser.Parse(queryString));

        // Assert
        options.UnsupportedModifierParams.ShouldHaveSingleItem().ShouldBe(expectedKey);
        options.UnsupportedParams.ShouldContain(expectedKey);
    }

    [Theory]
    [InlineData("status:above=final")]
    [InlineData("subject:above=Patient/1")]
    [InlineData("code:exact=x")]
    public void GivenAnUnsupportedModifierOnAnIndexedParameter_WhenBuilt_ThenItIsClassifiedTheSameWayAsOnAnIntrinsic(string queryString)
    {
        // Arrange -- the intrinsic/indexed split is a storage detail; R4's modifier rule does not know
        // about it, so the classification must not diverge across it.
        var expectedKey = queryString.Split('=')[0];

        // Act
        var options = OptionsBuilder.Build("Observation", QueryParser.Parse(queryString));

        // Assert
        options.UnsupportedModifierParams.ShouldHaveSingleItem().ShouldBe(expectedKey);
    }

    [Fact]
    public void GivenAnUnknownParameter_WhenBuilt_ThenItIsUnsupportedButNotAnUnsupportedModifier()
    {
        // Arrange -- the control for the classification: an unknown parameter is the case R4 says SHOULD
        // be ignored, so it must stay out of the list that drives rejection.
        // Act
        var options = OptionsBuilder.Build("Observation", QueryParser.Parse("nosuchparameter=x"));

        // Assert
        options.UnsupportedParams.ShouldContain("nosuchparameter");
        options.UnsupportedModifierParams.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenASupportedModifierOnTheSameIntrinsicParameter_WhenCompiled_ThenItStillCompilesToItsPredicate()
    {
        // Arrange -- without this control, "_id:above is rejected" would pass just as well if _id had been
        // broken outright. _id:not is the supported modifier on the same parameter.
        // Act
        var options = OptionsBuilder.Build("Observation", QueryParser.Parse("_id:not=abc"));
        var plan = await CompileAsync("Observation", "_id:not=abc");

        // Assert
        options.UnsupportedModifierParams.ShouldBeEmpty();
        options.UnsupportedParams.ShouldBeEmpty();
        plan.OuterPredicate.ShouldBeOfType<Predicate.Not>()
            .Operand.ShouldBeOfType<Predicate.Equal>()
            .Column.Column.ShouldBe("ResourceId");
    }

    [Fact]
    public async Task GivenAnUnsupportedModifierOnAnIntrinsicParameter_WhenCompiled_ThenThePlanCarriesNoPredicateAtAll()
    {
        // Arrange -- the reason the classification has to exist. Nothing in the plan records that a filter
        // was requested, so no downstream stage can recover the fact.
        // Act
        var plan = await CompileAsync("Observation", "_id:above=abc");

        // Assert
        plan.OuterPredicate.ShouldBeNull();
        plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ResourceSource>();
    }

    private static async Task<QueryPlan> CompileAsync(string? resourceType, string queryString)
    {
        var compiler = new SearchSqlCompiler(new CorpusSymbolResolver(), OptionsBuilder, searchParameterDefinitionManager: Definitions);
        var result = await compiler.TryCreatePlanAsync(
            resourceType,
            QueryParser.Parse(queryString),
            new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.None },
            CancellationToken.None);

        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        return result.Plan!.Query;
    }
}
