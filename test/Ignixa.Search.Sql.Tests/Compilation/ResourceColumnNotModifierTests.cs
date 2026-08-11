using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.Corpus;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Serialization.Abstractions;
using Ignixa.Specification.Generated;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

/// <summary>
/// Pins that <c>_id:not</c> and <c>_type:not</c> compile to the same negated outer predicate whether the
/// query names one value or several.
/// <para>
/// The two spellings do not reach Lower as the same expression. SearchExpressionBinder.BindAlternatives
/// lifts the <c>:not</c> off a comma list and wraps the resulting Or in a NotExpression; BindAtomic, which
/// handles a lone value, has no Or to lift onto and leaves the modifier sitting on the predicate. Lower's
/// resource-column extraction only understood the first shape, so <c>_id:not=a</c> reached
/// ResourceColumnLoweringRule with a modifier still attached and was rejected outright (HTTP 400) while
/// <c>_id:not=a,b</c> compiled fine.
/// </para>
/// <para>
/// The tests that go through <see cref="SearchSqlCompiler"/> and the real R4 definitions are the ones that
/// establish which shape each spelling actually produces; a hand-built expression tree can assert a shape
/// the binder never emits, which is how the gap survived earlier unit coverage.
/// </para>
/// </summary>
public class ResourceColumnNotModifierTests
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
    public async Task GivenASingleValuedIdNotModifier_WhenCompiled_ThenTheOuterWhereNegatesTheResourceIdEquality()
    {
        // Act
        var plan = await CompileAsync("Observation", "_id:not=abc");

        // Assert -- Not(ResourceId = @p), and no search-param CTE: _id never leaves dbo.Resource.
        var not = plan.OuterPredicate.ShouldBeOfType<Predicate.Not>();
        var equal = not.Operand.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("ResourceId");
        equal.Value.Value.ShouldBe("abc");
        plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ResourceSource>();
    }

    [Fact]
    public async Task GivenAMultiValuedIdNotModifier_WhenCompiled_ThenTheOuterWhereNegatesTheOrOfResourceIdEqualities()
    {
        // Act
        var plan = await CompileAsync("Observation", "_id:not=abc,def");

        // Assert
        var not = plan.OuterPredicate.ShouldBeOfType<Predicate.Not>();
        var or = not.Operand.ShouldBeOfType<Predicate.Or>();
        or.Left.ShouldBeOfType<Predicate.Equal>().Value.Value.ShouldBe("abc");
        or.Right.ShouldBeOfType<Predicate.Equal>().Value.Value.ShouldBe("def");
        plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ResourceSource>();
    }

    [Fact]
    public async Task GivenASingleValuedTypeNotModifier_WhenCompiledAtSystemLevel_ThenTheOuterWhereNegatesTheResourceTypeIdEquality()
    {
        // Act -- GET /?_type:not=Patient, the system-level sibling of the _id case above.
        var plan = await CompileAsync(resourceType: null, "_type:not=Patient");

        // Assert
        var not = plan.OuterPredicate.ShouldBeOfType<Predicate.Not>();
        not.Operand.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceTypeId");
    }

    [Fact]
    public async Task GivenAMultiValuedTypeNotModifier_WhenCompiledAtSystemLevel_ThenTheOuterWhereNegatesTheOrOfResourceTypeIdEqualities()
    {
        // Act
        var plan = await CompileAsync(resourceType: null, "_type:not=Patient,Organization");

        // Assert
        var not = plan.OuterPredicate.ShouldBeOfType<Predicate.Not>();
        var or = not.Operand.ShouldBeOfType<Predicate.Or>();
        or.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceTypeId");
        or.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceTypeId");
    }

    [Fact]
    public void GivenTheTwoNegationShapesTheBinderProduces_WhenLowered_ThenTheyProduceTheSamePlan()
    {
        // Arrange -- the same query, _id:not=abc, in the two forms the binder can hand to Lower: the
        // modifier left on the predicate (what BindAtomic emits for a lone value) and the modifier lifted
        // into a NotExpression around a one-element Or (what BindAlternatives would emit if the value list
        // were reached that way). Equivalence has to be asserted here rather than through the binder,
        // because no query string makes the binder produce a one-element Or.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var value = new TokenSearchValue(system: null, code: "abc", text: null);

        var modifierOnPredicate = new SearchParameterExpression(
            idParam,
            new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), value));

        var modifierLiftedIntoNot = new SearchParameterExpression(
            idParam,
            new NotExpression(Expression.Or(
            [
                new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, value),
            ])));

        // Act
        var fromPredicate = LowerSingleType(modifierOnPredicate);
        var fromNotExpression = LowerSingleType(modifierLiftedIntoNot);

        // Assert
        fromPredicate.Explain().ShouldBe(fromNotExpression.Explain());
        fromPredicate.OuterPredicate.ShouldBeOfType<Predicate.Not>()
            .Operand.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceId");
    }

    [Fact]
    public void GivenAnIdModifierOtherThanNot_WhenLowered_ThenLoweringStillRefusesIt()
    {
        // Arrange -- stripping :not must not have opened the door for every other modifier. _id:above is
        // meaningless on a resource id, and lowering it as though the modifier were absent would return a
        // positive _id match. Asserted at the Lower boundary because SearchOptionsBuilder rejects an
        // unsupported modifier at parse time and never hands it to the compiler (see
        // UnsupportedModifierClassificationTests), so a query-string test would prove nothing about this
        // guard -- it exists for a caller that builds the expression tree directly.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var tree = new SearchParameterExpression(
            idParam,
            new SearchParameterPredicateExpression(
                idParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Above), new TokenSearchValue(system: null, code: "abc", text: null)));

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerSingleType(tree));
    }

    private static QueryPlan LowerSingleType(Expression expression)
    {
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Observation"] = 96 });

        return LowerHarness.Run(
            expression,
            symbols,
            targetResourceType: "Observation",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            sortPhase: SortPhase.Valued,
            page: null).Plan;
    }

    private static async Task<QueryPlan> CompileAsync(string? resourceType, string queryString)
    {
        var result = await TryCompileAsync(resourceType, queryString);
        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        return result.Plan!.Query;
    }

    private static Task<SearchPlanResult> TryCompileAsync(string? resourceType, string queryString)
    {
        var compiler = new SearchSqlCompiler(new CorpusSymbolResolver(), OptionsBuilder, searchParameterDefinitionManager: Definitions);
        return compiler.TryCreatePlanAsync(
            resourceType,
            QueryParser.Parse(queryString),
            new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.None },
            CancellationToken.None);
    }
}
