using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Tests.Corpus;
using Ignixa.Serialization.Abstractions;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

/// <summary>
/// Pins <c>identifier:of-type</c> from query string to plan, through the real R4 definitions and the real
/// parser. The rule-level tests assert the predicate this compiles to; these assert that a request actually
/// reaches it — the binder used to flatten the typed value into field-level StringExpressions the SQL
/// compiler has no rule for, so every <c>:of-type</c> request failed compilation and surfaced as a 400.
/// </summary>
public class OfTypeCompilationTests
{
    private const string V2IdentifierType = "http://terminology.hl7.org/CodeSystem/v2-0203";

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
    public void GivenAnOfTypeQuery_WhenOptionsAreBuilt_ThenTheModifierIsNotClassifiedAsUnsupported()
    {
        // Arrange -- :of-type must not land in the bucket that drives a 400 for unsupported modifiers

        // Act
        var options = OptionsBuilder.Build("Patient", QueryParser.Parse($"identifier:of-type={V2IdentifierType}|MR|12345"));

        // Assert
        options.UnsupportedModifierParams.ShouldBeEmpty();
        options.UnsupportedParams.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAnOfTypeQueryWithATypeSystem_WhenCompiled_ThenOneTokenSourceCarriesAllThreeConditions()
    {
        // Act
        var plan = await CompileAsync($"identifier:of-type={V2IdentifierType}|MR|12345");

        // Assert -- a single ParamSource, not an intersection of one source per condition
        var source = plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ParamSource>();
        source.Table.TableName.ShouldBe("TokenSearchParam");

        var outer = source.Predicate.ShouldBeOfType<Predicate.And>();
        outer.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("IdentifierTypeSystemId");

        var inner = outer.Right.ShouldBeOfType<Predicate.And>();
        var typeCode = inner.Left.ShouldBeOfType<Predicate.Equal>();
        typeCode.Column.Column.ShouldBe("IdentifierTypeCode");
        typeCode.Value.Value.ShouldBe("MR");

        var value = inner.Right.ShouldBeOfType<Predicate.Equal>();
        value.Column.Column.ShouldBe("Code");
        value.Value.Value.ShouldBe("12345");
    }

    [Fact]
    public async Task GivenAnOfTypeQueryWithoutATypeSystem_WhenCompiled_ThenNoTypeSystemConditionIsEmitted()
    {
        // Act
        var plan = await CompileAsync("identifier:of-type=|MR|12345");

        // Assert
        var source = plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ParamSource>();
        var and = source.Predicate.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("IdentifierTypeCode");
        and.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("Code");
    }

    [Fact]
    public async Task GivenCommaSeparatedOfTypeValues_WhenCompiled_ThenTheyBecomeAUnionOfPerValueSources()
    {
        // Arrange -- comma is OR in FHIR, so the alternatives must stay separate sources; folding them into
        // one source's conjunction would silently require both identifiers on the same row.

        // Act
        var plan = await CompileAsync($"identifier:of-type={V2IdentifierType}|MR|12345,{V2IdentifierType}|SS|67890");

        // Assert
        plan.Ctes.OfType<CteDefinition.Union>().ShouldHaveSingleItem().Parts.Count.ShouldBe(2);
        plan.Ctes.OfType<CteDefinition.ParamSource>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnOfTypeQuery_WhenCompiled_ThenNoUserValueAppearsInTheSqlText()
    {
        // Arrange -- the identifier type system reaches SQL as a resolved integer id, the type code and the
        // identifier value as parameters. A literal in the text would be both an injection surface and a
        // plan-cache pollutant.

        // Act
        var emitted = await EmitAsync($"identifier:of-type={V2IdentifierType}|MR|12345");

        // Assert
        emitted.Sql.ShouldNotContain(V2IdentifierType);
        emitted.Sql.ShouldNotContain("12345");
        emitted.Parameters.Select(p => p.Value).ShouldContain("MR");
        emitted.Parameters.Select(p => p.Value).ShouldContain("12345");
    }

    private static async Task<QueryPlan> CompileAsync(string queryString)
    {
        var compiler = new SearchSqlCompiler(new CorpusSymbolResolver(), OptionsBuilder, searchParameterDefinitionManager: Definitions);
        var result = await compiler.TryCreatePlanAsync(
            "Patient",
            QueryParser.Parse(queryString),
            new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.None },
            CancellationToken.None);

        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        return result.Plan!.Query;
    }

    private static async Task<Sql.Builders.EmittedSql> EmitAsync(string queryString)
        => Sql.Builders.SqlBuilder.Run(await CompileAsync(queryString));
}
