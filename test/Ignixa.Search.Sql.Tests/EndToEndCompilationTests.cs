using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests;

public class EndToEndCompilationTests
{
    private sealed class FakeSymbolResolver : ISymbolResolver
    {
        public Dictionary<string, short> SearchParamIds { get; } = [];
        public Dictionary<string, short> ResourceTypeIds { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult(parameter.Url?.ToString() is { } url && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);
    }

    [Fact]
    public async Task GivenAPatientNameExactAndActiveQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Patient?name:exact=Smith&active=true
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith")),
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null)),
        ]);
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, top: 10);
        var emitted = Emit.Run(plan);

        // Assert -- the plan-shape golden test
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[202]  Text = @p0 collate CS_AS\n" +
            "cte1 = TokenSearchParam[44]  Code = @p1\n" +
            "root = Intersect(cte0, cte1) top 10");

        // Assert -- no user value ever appears in SQL text
        emitted.Sql.ShouldNotContain("Smith");
        emitted.Sql.ShouldNotContain("true");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)"Smith"), ("@p1", (object)"true")]);
    }

    [Fact]
    public async Task GivenAValueSetUrlQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- ValueSet?url=http://example.org/fhir/ValueSet/1
        var urlParam = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            urlParam, SearchComparator.Eq, modifier: null, new UriSearchValue("http://example.org/fhir/ValueSet/1", separateCanonicalComponents: false));
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[urlParam.Url!.ToString()] = 88;

        // Act
        var symbolTable = await Resolve.RunAsync(predicate, resolver, CancellationToken.None);
        var plan = Lower.Run(predicate, symbolTable);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = UriSearchParam[88]  Uri = @p0");
        emitted.Sql.ShouldNotContain("example.org");
        emitted.Parameters.ShouldContain(p => p.Value.Equals("http://example.org/fhir/ValueSet/1"));
    }

    [Fact]
    public async Task GivenAnObservationDateRangeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?date=ge2023-01-01&value-quantity=gt5.4
        var dateParam = new SearchParameterInfo("date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));
        var quantityParam = new SearchParameterInfo("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-quantity"));
        var dateValue = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterPredicateExpression(dateParam, SearchComparator.Ge, modifier: null, dateValue),
            new SearchParameterPredicateExpression(quantityParam, SearchComparator.Gt, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m)),
        ]);
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[dateParam.Url!.ToString()] = 203;
        resolver.SearchParamIds[quantityParam.Url!.ToString()] = 204;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe(
            "cte0 = DateTimeSearchParam[203]  EndDateTime >= @p0\n" +
            "cte1 = QuantitySearchParam[204]  LowValue > @p1\n" +
            "root = Intersect(cte0, cte1)");
        emitted.Sql.ShouldNotContain("2023");
        emitted.Parameters.ShouldContain(p => p.Value.Equals(dateValue.Start));
        emitted.Parameters.ShouldContain(p => p.Value.Equals(5.4m));
    }
}
