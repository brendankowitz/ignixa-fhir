using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.TestSupport;

internal static class PlanFixtures
{
    public static readonly SearchParameterInfo NameParameter = new(
        "name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

    /// <summary>A plain <c>Patient?name:exact=Smith</c>.</summary>
    public static async Task<QueryPlan> SimplePatientSearchAsync()
    {
        var expression = new SearchParameterPredicateExpression(
            NameParameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[NameParameter.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        var symbols = (await ResolveHarness.RunAsync(expression, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;

        return LowerHarness.Run(expression, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
    }

    /// <summary>
    /// A plan that cannot be emitted: an includes-only page with no include stages, which
    /// <c>SqlBuilder.RejectUnsupportedCombinations</c> refuses because it can only ever return nothing.
    /// </summary>
    public static async Task<QueryPlan> IncoherentPlanAsync()
        => await SimplePatientSearchAsync() with { Shape = new ResultShape.IncludesPage() };

    /// <summary>An expression no query string can produce, standing in for a FHIR operation root.</summary>
    public static Expression EverythingExpression()
        => new SearchParameterExpression(
            NameParameter,
            new SearchParameterPredicateExpression(
                NameParameter, SearchComparator.Eq, modifier: null, new StringSearchValue("operation-root")));
}
