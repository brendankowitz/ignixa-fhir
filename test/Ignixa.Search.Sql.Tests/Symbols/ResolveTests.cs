using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Symbols;

public class ResolveTests
{
    [Fact]
    public async Task GivenATreeWithOnePredicate_WhenResolved_ThenSymbolTableHasItsSearchParamId()
    {
        // Arrange
        var parameter = new SearchParameterInfo(
            "name",
            "name",
            SearchParamType.String,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Patient-name"] = 202;

        // Act
        var symbolTable = await Resolve.RunAsync(predicate, resolver, CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(parameter).ShouldBe((short)202);
    }

    [Fact]
    public async Task GivenACompositeTree_WhenResolved_ThenBothComponentsAreResolved()
    {
        // Arrange -- matches the tree shape SearchExpressionBinder builds for a composite parameter:
        // SearchParameterExpression(composite, MultiaryExpression(And, [CompositeComponentExpression...]))
        var codeParam = new SearchParameterInfo(
            "component-code",
            "component-code",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code"));
        var quantityParam = new SearchParameterInfo(
            "component-value-quantity",
            "component-value-quantity",
            SearchParamType.Quantity,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-value-quantity"));
        var compositeParam = new SearchParameterInfo(
            "component-code-value-quantity",
            "component-code-value-quantity",
            SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"));

        var codePredicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue("http://loinc.org", "8480-6", text: null));
        var quantityPredicate = new SearchParameterPredicateExpression(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 107m));

        var codeComponent = new CompositeComponentExpression(codeParam, 0, codePredicate);
        var quantityComponent = new CompositeComponentExpression(quantityParam, 1, quantityPredicate);

        var and = new MultiaryExpression(MultiaryOperator.And, [codeComponent, quantityComponent]);
        var composite = new SearchParameterExpression(compositeParam, and);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-component-code"] = 401;
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-component-value-quantity"] = 402;

        // Act
        var symbolTable = await Resolve.RunAsync(composite, resolver, CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(codeParam).ShouldBe((short)401);
        symbolTable.SearchParamId(quantityParam).ShouldBe((short)402);
    }

    [Fact]
    public async Task GivenAParameterTheResolverCannotFind_WhenResolved_ThenItIsSimplyAbsentFromTheTable()
    {
        // Arrange -- the fake resolver has no row for this parameter at all.
        var parameter = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Patient/123"));
        var resolver = new FakeSymbolResolver();

        // Act -- Resolve itself must not throw for an unresolvable parameter.
        var symbolTable = await Resolve.RunAsync(predicate, resolver, CancellationToken.None);

        // Assert -- the miss only surfaces when something actually looks the parameter up later.
        Should.Throw<KeyNotFoundException>(() => symbolTable.SearchParamId(parameter));
    }

    /// <summary>
    /// An in-memory, dictionary-backed <see cref="ISymbolResolver"/> -- not a mock, a real (if
    /// trivial) implementation, matching this repo's testing philosophy of exercising real
    /// behavior rather than recorded expectations.
    /// </summary>
    private sealed class FakeSymbolResolver : ISymbolResolver
    {
        public Dictionary<string, short> SearchParamIds { get; } = [];

        public Dictionary<string, short> ResourceTypeIds { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
        {
            var url = parameter.Url?.ToString();
            return Task.FromResult(url != null && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);
        }

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);
    }
}
