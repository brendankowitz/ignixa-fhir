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
}
