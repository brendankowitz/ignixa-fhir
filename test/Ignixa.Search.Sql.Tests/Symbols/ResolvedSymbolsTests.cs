using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Symbols;

public class ResolvedSymbolsTests
{
    private sealed class NullResolver : ISymbolResolver
    {
        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult<short?>(null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult<short?>(103);
    }

    [Fact]
    public async Task GivenAnUnresolvableParameter_WhenResolved_ThenItIsReportedAsUnresolved()
    {
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://x/name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, null, new StringSearchValue("Smith"));

        var resolved = await Resolve.RunAsync(
            predicate, includes: [], revIncludes: [], sort: [], new NullResolver(), "Patient", CancellationToken.None);

        resolved.Unresolved.ShouldContain(p => p.Code == "name");
    }

    private sealed class AlwaysResolvingResolver : ISymbolResolver
    {
        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult<short?>(202);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult<short?>(103);
    }

    [Fact]
    public async Task GivenAParameterWithNoUrl_WhenResolved_ThenItIsReportedUnresolvedRatherThanThrowing()
    {
        // SymbolTable is keyed by Url, so a parameter without one can never be looked up even when the
        // resolver hands back an id -- SymbolTable.SearchParamId says exactly that. Resolve used to
        // dereference the null Url instead, and the NullReferenceException escaped SearchCompiler's
        // catch (which only handles NotSupportedException/KeyNotFoundException), killing the whole trace.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, null, new StringSearchValue("Smith"));

        var resolved = await Resolve.RunAsync(
            predicate, includes: [], revIncludes: [], sort: [], new AlwaysResolvingResolver(), "Patient", CancellationToken.None);

        resolved.Unresolved.ShouldContain(p => p.Code == "name");
    }
}
