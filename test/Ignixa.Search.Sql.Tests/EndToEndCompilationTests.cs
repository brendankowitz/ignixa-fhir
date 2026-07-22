using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using SortOrder = Ignixa.Search.Expressions.SortOrder;

namespace Ignixa.Search.Sql.Tests;

public class EndToEndCompilationTests
{
    private sealed class FakeSymbolResolver : ISymbolResolver
    {
        public Dictionary<string, short> SearchParamIds { get; } = [];
        public Dictionary<string, short> ResourceTypeIds { get; } = [];
        public Dictionary<string, int> SystemIds { get; } = [];
        public Dictionary<string, int> QuantityCodeIds { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult(parameter.Url?.ToString() is { } url && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);

        public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
            => Task.FromResult(SystemIds.TryGetValue(system, out var id) ? (int?)id : null);

        public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(QuantityCodeIds.TryGetValue(code, out var id) ? (int?)id : null);
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
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null, top: 10).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the plan-shape golden test
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[103,202]  Text = @p0 collate CS_AS\n" +
            "cte1 = TokenSearchParam[103,44]  Code = @p1\n" +
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
        resolver.ResourceTypeIds["ValueSet"] = 105;

        // Act
        var symbolTable = (await Resolve.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "ValueSet", CancellationToken.None)).Symbols;
        var plan = Lower.Run(predicate, symbolTable, targetResourceType: "ValueSet", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = UriSearchParam[105,88]  Uri = @p0 collate Latin1_General_100_BIN2");
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
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert
        plan.Explain().ShouldBe(
            "cte0 = DateTimeSearchParam[104,203]  EndDateTime >= @p0\n" +
            "cte1 = QuantitySearchParam[104,204]  LowValue > @p1\n" +
            "root = Intersect(cte0, cte1)");
        emitted.Sql.ShouldNotContain("2023");
        emitted.Parameters.ShouldContain(p => p.Value.Equals(dateValue.Start));
        emitted.Parameters.ShouldContain(p => p.Value.Equals(5.4m));
    }

    [Fact]
    public async Task GivenAnOrdinaryQueryWrappedInSearchParameterExpression_WhenCompiled_ThenUnwrapsToTheSamePlanAsTheBareLeaf()
    {
        // Arrange -- ValueSet?url=... as the real binder actually produces it: SearchParameterExpression(param, predicate),
        // not the bare predicate GivenAValueSetUrlQuery_... hand-builds. Proves Lower's SearchParameterExpression case
        // isn't only reachable for composites -- it's the general unwrap every real query goes through.
        var urlParam = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            urlParam, SearchComparator.Eq, modifier: null, new UriSearchValue("http://example.org/fhir/ValueSet/1", separateCanonicalComponents: false));
        var tree = new SearchParameterExpression(urlParam, predicate);
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[urlParam.Url!.ToString()] = 88;
        resolver.ResourceTypeIds["ValueSet"] = 105;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "ValueSet", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "ValueSet", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- identical plan shape to the bare-predicate case above (same table, same SearchParamId)
        plan.Explain().ShouldBe("root = UriSearchParam[105,88]  Uri = @p0 collate Latin1_General_100_BIN2");
        emitted.Sql.ShouldNotContain("example.org");
        emitted.Parameters.ShouldContain(p => p.Value.Equals("http://example.org/fhir/ValueSet/1"));
    }

    [Fact]
    public async Task GivenAnObservationTokenTokenCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?code-value-concept=8480-6$high
        var compositeParam = new SearchParameterInfo(
            "code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var valueParam = new SearchParameterInfo("value-concept", "value-concept", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-concept"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(valueParam, 1,
                    new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "high", text: null))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 301;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = TokenTokenCompositeSearchParam[104,301]  Code1 = @p0 AND Code2 = @p1");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Sql.ShouldNotContain("high");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)"8480-6"), ("@p1", (object)"high")]);
    }

    [Fact]
    public async Task GivenAnObservationTokenNumberNumberCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?component-code-value-number-number=8480-6$ge5$le10
        var compositeParam = new SearchParameterInfo(
            "component-code-value-number-number", "component-code-value-number-number", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-component-code-value-number-number"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/Observation-code"));
        var lowParam = new SearchParameterInfo("low", "low", SearchParamType.Number, new Uri("http://example.org/fhir/SearchParameter/Observation-low"));
        var highParam = new SearchParameterInfo("high", "high", SearchParamType.Number, new Uri("http://example.org/fhir/SearchParameter/Observation-high"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(lowParam, 1,
                    new SearchParameterPredicateExpression(lowParam, SearchComparator.Ge, modifier: null, new NumberSearchValue(5m))),
                new CompositeComponentExpression(highParam, 2,
                    new SearchParameterPredicateExpression(highParam, SearchComparator.Le, modifier: null, new NumberSearchValue(10m))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 302;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = TokenNumberNumberCompositeSearchParam[104,302]  Code1 = @p0 AND LowValue2 >= @p1 AND HighValue3 <= @p2");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)"8480-6"), ("@p1", 5m), ("@p2", 10m)]);
    }

    [Fact]
    public async Task GivenACommaSeparatedCompositeAlternatives_WhenCompiled_ThenUnionsOneParamSourcePerAlternative()
    {
        // Arrange -- Observation?code-value-concept=A$1,B$2 (two comma-separated composite values -- SearchParameterExpression(composite, Or(And(...), And(...))))
        var compositeParam = new SearchParameterInfo(
            "code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var valueParam = new SearchParameterInfo("value-concept", "value-concept", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-concept"));

        CompositeComponentExpression[] Alternative(string code, string value) =>
        [
            new(codeParam, 0, new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null))),
            new(valueParam, 1, new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: value, text: null))),
        ];

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.Or,
            [
                new MultiaryExpression(MultiaryOperator.And, Alternative("A", "1")),
                new MultiaryExpression(MultiaryOperator.And, Alternative("B", "2")),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 301;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- two ParamSource CTEs (one per alternative), unioned at the root
        plan.Explain().ShouldBe(
            "cte0 = TokenTokenCompositeSearchParam[104,301]  Code1 = @p0 AND Code2 = @p1\n" +
            "cte1 = TokenTokenCompositeSearchParam[104,301]  Code1 = @p2 AND Code2 = @p3\n" +
            "root = Union(cte0, cte1)");
    }

    [Fact]
    public async Task GivenAnObservationTokenStringCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?code-value-string=8480-6$Elevated
        var compositeParam = new SearchParameterInfo(
            "code-value-string", "code-value-string", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-code-value-string"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/Observation-code"));
        var valueParam = new SearchParameterInfo("value-string", "value-string", SearchParamType.String, new Uri("http://example.org/fhir/SearchParameter/Observation-value-string"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(valueParam, 1,
                    new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Elevated"))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 401;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = TokenStringCompositeSearchParam[104,401]  Code1 = @p0 AND Text2 LIKE @p1 (StartsWith) collate CI_AI");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Sql.ShouldNotContain("Elevated");
    }

    [Fact]
    public async Task GivenAnObservationTokenQuantityCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?component-code-value-quantity=8480-6$120
        var compositeParam = new SearchParameterInfo(
            "component-code-value-quantity", "component-code-value-quantity", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"));
        var codeParam = new SearchParameterInfo("component-code", "component-code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code"));
        var quantityParam = new SearchParameterInfo("component-value-quantity", "component-value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-value-quantity"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(quantityParam, 1,
                    new SearchParameterPredicateExpression(quantityParam, SearchComparator.Ge, modifier: null, new QuantitySearchValue(system: null!, code: null!, 120m))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 402;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- Ge (not Eq) so the raw value is used directly, no precision-widening bounds to compute
        plan.Explain().ShouldBe("root = TokenQuantityCompositeSearchParam[104,402]  Code1 = @p0 AND LowValue2 >= @p1");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Parameters.ShouldContain(p => p.Value.Equals(120m));
    }

    [Fact]
    public async Task GivenAnObservationTokenDateTimeCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?code-value-date=8480-6$ge2023-01-01
        var compositeParam = new SearchParameterInfo(
            "code-value-date", "code-value-date", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-code-value-date"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/Observation-code"));
        var dateParam = new SearchParameterInfo("value-date", "value-date", SearchParamType.Date, new Uri("http://example.org/fhir/SearchParameter/Observation-value-date"));
        var dateValue = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(dateParam, 1,
                    new SearchParameterPredicateExpression(dateParam, SearchComparator.Ge, modifier: null, dateValue)),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 403;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = TokenDateTimeCompositeSearchParam[104,403]  Code1 = @p0 AND EndDateTime2 >= @p1");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Parameters.ShouldContain(p => p.Value.Equals(dateValue.Start));
    }

    [Fact]
    public async Task GivenADocumentReferenceRelatesToCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- DocumentReference?relatesto=replaces$DocumentReference/456
        var compositeParam = new SearchParameterInfo(
            "relatesto", "relatesto", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/DocumentReference-relatesto"));
        var targetParam = new SearchParameterInfo("target", "target", SearchParamType.Reference, new Uri("http://example.org/fhir/SearchParameter/DocumentReference-target"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/DocumentReference-code"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(targetParam, 0,
                    new SearchParameterPredicateExpression(targetParam, SearchComparator.Eq, modifier: null,
                        new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: "456"))),
                new CompositeComponentExpression(codeParam, 1,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "replaces", text: null))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 404;
        resolver.ResourceTypeIds["DocumentReference"] = 55;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "DocumentReference", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "DocumentReference", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert
        plan.Explain().ShouldBe(
            "root = ReferenceTokenCompositeSearchParam[55,404]  BaseUri1 IS NULL AND ReferenceResourceTypeId1 = @p0 AND ReferenceResourceId1 = @p1 AND Code2 = @p2");
        emitted.Sql.ShouldNotContain("456");
        emitted.Sql.ShouldNotContain("replaces");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)(short)55), ("@p1", (object)"456"), ("@p2", (object)"replaces")]);
    }

    [Fact]
    public async Task GivenAPatientNameNotQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Patient?name:not=Smith (single value -- the binder gives this a bare predicate
        // with Modifier.SearchModifierCode == Not, NOT a NotExpression wrapper; confirmed against
        // SearchPredicateExpressionBuilder.cs and the real binder's single-value path)
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var tree = new SearchParameterExpression(
            nameParam,
            new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new StringSearchValue("Smith")));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[103,202]  Text LIKE @p0 (StartsWith) collate CI_AI\n" +
            "cte1 = ResourceSource[103]\n" +
            "root = Except(cte1, cte0)");
        emitted.Sql.ShouldContain("NOT EXISTS");
        emitted.Sql.ShouldNotContain("Smith");
    }

    [Fact]
    public async Task GivenAPatientActiveAndNameNotQuery_WhenCompiled_ThenIntersectsTheExceptResult()
    {
        // Arrange -- Patient?active=true&name:not=Smith,Jones (comma-separated -- the binder wraps
        // this as NotExpression(Or([predicate-per-alternative])), each alternative losing its own
        // modifier per BindAlternatives' itemModifier: null)
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null)),
            new SearchParameterExpression(
                nameParam,
                new NotExpression(new MultiaryExpression(MultiaryOperator.Or,
                [
                    new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith")),
                    new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Jones")),
                ]))),
        ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- one CTE for `active`, a Union of the two Smith/Jones alternatives, ResourceSource, Except, then an outer Intersect
        plan.Explain().ShouldBe(
            "cte0 = TokenSearchParam[103,44]  Code = @p0\n" +
            "cte1 = StringSearchParam[103,202]  Text LIKE @p1 (StartsWith) collate CI_AI\n" +
            "cte2 = StringSearchParam[103,202]  Text LIKE @p2 (StartsWith) collate CI_AI\n" +
            "cte3 = Union(cte1, cte2)\n" +
            "cte4 = ResourceSource[103]\n" +
            "cte5 = Except(cte4, cte3)\n" +
            "root = Intersect(cte0, cte5)");
        emitted.Sql.ShouldNotContain("Smith");
        emitted.Sql.ShouldNotContain("Jones");
        emitted.Sql.ShouldNotContain("true");
    }

    [Fact]
    public async Task GivenAPatientIdOnlyQuery_WhenCompiled_ThenUsesResourceSourceAsTheBaseSetWithAnOuterIdFilter()
    {
        // Arrange -- Patient?_id=123 (no other search parameters)
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var tree = new SearchParameterExpression(
            idParam,
            new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null)));

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- ResourceSource's own ResourceTypeId consumes @p0 (it's a real bound parameter in
        // Emit, and PlanExplainer's ordinal counter now accounts for it too), so the outer predicate is @p1
        plan.Explain().ShouldBe("root = ResourceSource[103] WHERE ResourceId = @p1");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        emitted.Sql.ShouldNotContain("123");
    }

    [Fact]
    public async Task GivenATypeQueryForADifferentResourceTypeThanTheTarget_WhenCompiled_ThenResolvesTheValuesOwnResourceTypeId()
    {
        // Arrange -- Patient?_type=Observation. Non-tautological on purpose: the query's own
        // targetResourceType ("Patient") differs from _type's value ("Observation"), so this only
        // compiles if Resolve collects _type's own TokenSearchValue.Code into the SymbolTable's
        // resource-type set -- targetResourceType alone would only ever resolve "Patient".
        var typeParam = new SearchParameterInfo("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));
        var tree = new SearchParameterExpression(
            typeParam,
            new SearchParameterPredicateExpression(typeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "Observation", text: null)));

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- ResourceSource seeds from the query's own target (Patient, 103); the outer WHERE
        // filters on _type's resolved value (Observation, 104) -- two different resolved ids in one plan.
        plan.Explain().ShouldBe("root = ResourceSource[103] WHERE ResourceTypeId = @p1");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        emitted.Parameters.ShouldContain(p => p.Value.Equals((short)104));
    }

    [Fact]
    public async Task GivenAPatientIdAndActiveQuery_WhenCompiled_ThenLowersActiveNormallyAndAppliesIdAsAnOuterFilter()
    {
        // Arrange -- Patient?_id=123&active=true
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null))),
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null)),
        ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- only `active` becomes a CTE; `_id` becomes the outer WHERE
        plan.Explain().ShouldBe("root = TokenSearchParam[103,44]  Code = @p0 WHERE ResourceId = @p1");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        emitted.Sql.ShouldNotContain("123");
        emitted.Sql.ShouldNotContain("true");
    }

    [Fact]
    public async Task GivenAMultiValueIdNotQuery_WhenCompiled_ThenThrowsRatherThanSilentlyRoutingIntoTokenSearchParam()
    {
        // Arrange -- Patient?_id:not=1,2 (comma-separated, so the binder wraps it as
        // SearchParameterExpression(idParam, NotExpression(Or([pred("1", Modifier:null), pred("2", Modifier:null)])))
        // -- the top-level extraction pass only recognizes a BARE SearchParameterPredicateExpression, so this
        // shape is never extracted; each Or alternative reaches StructuralContext.Lower's dispatch choke point
        // directly, where it must throw (a resource column has no TokenSearchParam row to match) rather than
        // silently routing "_id" into TokenSearchParam via the generic Token dispatch, which would silently
        // produce an always-empty (or always-true, once Except negates it) match instead of a loud failure.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var tree = new SearchParameterExpression(
            idParam,
            new NotExpression(new MultiaryExpression(MultiaryOperator.Or,
            [
                new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "1", text: null)),
                new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "2", text: null)),
            ])));

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act & Assert
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        Should.Throw<NotSupportedException>(() => Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null));
    }

    [Fact]
    public async Task GivenAPatientLastUpdatedExactInstantQuery_WhenCompiled_ThenAppliesItAsAnOuterFilter()
    {
        // Arrange -- Patient?_lastUpdated=2023-06-15T12:30:00.000Z
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var instant = new DateTimeOffset(2023, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var tree = new SearchParameterExpression(
            lastUpdatedParam,
            new SearchParameterPredicateExpression(lastUpdatedParam, SearchComparator.Ge, modifier: null, new DateTimeSearchValue(instant)));

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- ResourceSource's own ResourceTypeId consumes @p0, so the outer predicate is @p1
        plan.Explain().ShouldBe("root = ResourceSource[103] WHERE ResourceSurrogateId >= @p1");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        var expectedTicks = new DateTime(2023, 6, 15, 12, 30, 0, DateTimeKind.Utc).Ticks;
        emitted.Parameters.ShouldContain(p => p.Value.Equals(expectedTicks << 3));
    }

    [Fact]
    public async Task GivenAPatientIdAndNameNotQuery_WhenCompiled_ThenCombinesTheOuterFilterAndTheExceptResult()
    {
        // Arrange -- Patient?_id=123&name:not=Smith
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null))),
            new SearchParameterExpression(
                nameParam,
                new NotExpression(new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith")))),
        ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the :not's ResourceSource+Except becomes the match CTE; _id becomes the outer WHERE.
        // StringSearchParam consumes @p0 for its Text parameter. ResourceSource's ResourceTypeId consumes
        // @p1 (implicit counter increment). The outer WHERE ResourceId filter consumes @p2.
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[103,202]  Text LIKE @p0 (StartsWith) collate CI_AI\n" +
            "cte1 = ResourceSource[103]\n" +
            "root = Except(cte1, cte0) WHERE ResourceId = @p2");
        emitted.Sql.ShouldContain("NOT EXISTS");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        emitted.Sql.ShouldNotContain("Smith");
        emitted.Sql.ShouldNotContain("123");
    }

    [Fact]
    public async Task GivenAForwardChainQuery_WhenCompiled_ThenChainJoinsThroughTheReferenceTranslation()
    {
        // Arrange -- Patient?organization.name=Acme
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"));
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, new SearchParameterExpression(nameParam, innerPredicate));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = (await Resolve.RunAsync(chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the target-side match (Organization.name=Acme) becomes cte0, the ChainJoin is root.
        // No modifier on `name` means StringLoweringRule's default arm applies (StartsWith, CI_AI) --
        // same as the unmodified `name` predicate in GivenAPatientNameNotQuery above -- not a plain Equal.
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text LIKE @p0 (StartsWith) collate CI_AI\n" +
            "root = ChainJoin(cte0, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        emitted.Sql.ShouldContain("SELECT DISTINCT");
        emitted.Sql.ShouldNotContain("Acme");
        // Bound as "Acme%" (LikeMatch.StartsWith's escaped pattern), not a bare "Acme" -- StringLoweringRule's
        // default arm (no :exact modifier) always produces a LIKE, never a plain Equal; see the divergence note above.
        emitted.Parameters.ShouldContain(p => p.Value.Equals("Acme%"));
    }

    [Fact]
    public async Task GivenAReverseChainQuery_WhenCompiled_ThenChainJoinsWithOutputOnTheReferencedSide()
    {
        // Arrange -- Patient?_has:Observation:patient:code=1234-5
        var patientRefParam = new SearchParameterInfo("patient", "patient", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var innerPredicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "1234-5", text: null));
        var chain = new ChainedExpression(["Observation"], patientRefParam, ["Patient"], reversed: true, new SearchParameterExpression(codeParam, innerPredicate));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[patientRefParam.Url!.ToString()] = 77;
        resolver.SearchParamIds[codeParam.Url!.ToString()] = 88;
        resolver.ResourceTypeIds["Observation"] = 106;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the referencing-side match (Observation.code=1234-5) becomes cte0, the ChainJoin is root
        plan.Explain().ShouldBe(
            "cte0 = TokenSearchParam[106,88]  Code = @p0\n" +
            "root = ChainJoin(cte0, ref=77, inner=106, output=[103], Reverse)");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        emitted.Sql.ShouldNotContain("1234-5");
        emitted.Parameters.ShouldContain(p => p.Value.Equals("1234-5"));
    }

    [Fact]
    public async Task GivenANestedChainTwoLevelsDeep_WhenCompiled_ThenComposesTwoChainJoins()
    {
        // Arrange -- Patient?organization.partof.name=Acme (Organization.partOf is itself a reference to Organization)
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var partOfParam = new SearchParameterInfo("partof", "partof", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"));
        var innerChain = new ChainedExpression(["Organization"], partOfParam, ["Organization"], reversed: false, new SearchParameterExpression(nameParam, innerPredicate));
        var outerChain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, innerChain);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[partOfParam.Url!.ToString()] = 56;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = (await Resolve.RunAsync(outerChain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(outerChain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the innermost match (Organization.name=Acme) becomes cte0, the inner ChainJoin
        // (partof) becomes cte1 and is itself InnerMatch for the outer ChainJoin (organization).
        // No modifier on `name` means StringLoweringRule's default arm applies (StartsWith, CI_AI) --
        // same as the unmodified `name` predicate in GivenAForwardChainQuery above -- not a plain Equal.
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text LIKE @p0 (StartsWith) collate CI_AI\n" +
            "cte1 = ChainJoin(cte0, ref=56, inner=105, output=[105], Forward)\n" +
            "root = ChainJoin(cte1, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldNotContain("Acme");
    }

    [Fact]
    public void GivenAChainNestedMoreThan10LevelsDeep_WhenCompiled_ThenThrows()
    {
        // Arrange -- build a chain 11 levels deep by wrapping a leaf predicate in ChainedExpression 11 times
        var refParam = new SearchParameterInfo("ref", "ref", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Organization-ref"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"));
        Expression current = new SearchParameterExpression(nameParam, innerPredicate);
        for (var i = 0; i < 11; i++)
        {
            current = new ChainedExpression(["Organization"], refParam, ["Organization"], reversed: false, current);
        }

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[refParam.Url!.ToString()] = 60;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act & Assert -- Resolve doesn't need to run for this test; Lower's depth guard is what's under test
        var symbolTable = new SymbolTable(
            new Dictionary<string, short> { [refParam.Url!.ToString()] = 60, [nameParam.Url!.ToString()] = 202 },
            new Dictionary<string, short> { ["Organization"] = 105 });

        Should.Throw<NotSupportedException>(() => Lower.Run(current, symbolTable, targetResourceType: "Organization", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("10");
    }

    [Fact]
    public async Task GivenAForwardChainWithAMultiaryTargetExpression_WhenCompiled_ThenIntersectsBothTargetPredicates()
    {
        // Arrange -- proves Lower's Intersect/ChainJoin composition mechanism for a multiary chain
        // target directly at the IR level. Note: the real binder does NOT produce this shape for the
        // query string "Patient?organization.name=Acme&organization.active=true" today --
        // SearchOptionsBuilder.cs parses each top-level query parameter independently and ANDs the
        // results above/outside any chain (STEP 2, Expression.And(searchExpressions)), so that query
        // actually yields two separate ChainedExpression/ChainJoin nodes ANDed at the top level, not
        // one chain with an And-composed target. ChainedExpression.Expression is still typed as a
        // plain Expression, so Lower must handle a multiary target correctly regardless of whether
        // today's binder currently constructs one -- this test proves that mechanism in isolation.
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Organization-active"));
        var targetExpression = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(nameParam, new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"))),
            new SearchParameterExpression(activeParam, new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))),
        ]);
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, targetExpression);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = (await Resolve.RunAsync(chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- both target predicates intersect into one InnerMatch before the ChainJoin.
        // No modifier on `name` means StringLoweringRule's default arm applies (StartsWith, CI_AI) --
        // same as the unmodified `name` predicate in GivenAForwardChainQuery above -- not a plain Equal.
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text LIKE @p0 (StartsWith) collate CI_AI\n" +
            "cte1 = TokenSearchParam[105,44]  Code = @p1\n" +
            "cte2 = Intersect(cte0, cte1)\n" +
            "root = ChainJoin(cte2, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldNotContain("Acme");
        emitted.Sql.ShouldNotContain("true");
    }

    [Fact]
    public async Task GivenAForwardChainWithAResourceColumnPredicateOnTheTarget_WhenCompiled_ThenIntersectsAFilteredResourceSource()
    {
        // Arrange -- Patient?organization._id=org-1
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var targetExpression = new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "org-1", text: null)));
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, targetExpression);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = (await Resolve.RunAsync(chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the target scope's _id predicate becomes a filtered ResourceSource (not OuterPredicate,
        // which only applies at the true top level), the ChainJoin's InnerMatch is that ResourceSource directly
        // (no Intersect needed since _id was the target expression's only predicate).
        plan.Explain().ShouldBe(
            "cte0 = ResourceSource[105] WHERE ResourceId = @p1\n" +
            "root = ChainJoin(cte0, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldNotContain("org-1");
    }

    [Fact]
    public async Task GivenAReverseChainWithAResourceColumnPredicateOnTheReferencingSide_WhenCompiled_ThenIntersectsAFilteredResourceSource()
    {
        // Arrange -- Patient?_has:Observation:patient:_id=obs-1
        var patientRefParam = new SearchParameterInfo("patient", "patient", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"));
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var targetExpression = new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "obs-1", text: null)));
        var chain = new ChainedExpression(["Observation"], patientRefParam, ["Patient"], reversed: true, targetExpression);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[patientRefParam.Url!.ToString()] = 77;
        resolver.ResourceTypeIds["Observation"] = 106;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await Resolve.RunAsync(chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- identical mechanism to the forward case, just on the referencing (inner) side this time
        plan.Explain().ShouldBe(
            "cte0 = ResourceSource[106] WHERE ResourceId = @p1\n" +
            "root = ChainJoin(cte0, ref=77, inner=106, output=[103], Reverse)");
        emitted.Sql.ShouldNotContain("obs-1");
    }

    [Fact]
    public async Task GivenAForwardChainWithBothAResourceColumnAndAnOrdinaryPredicateOnTheTarget_WhenCompiled_ThenIntersectsTheFilteredResourceSourceWithTheOrdinaryMatch()
    {
        // Arrange -- Patient?organization._id=org-1&organization.name=Acme -- exercises LowerScopedExpression's
        // Intersect branch (the two tests above only cover the remaining-is-null / predicate-only case). A task
        // review flagged this as untested given this codebase's history of the "predicate silently dropped"
        // bug class recurring across multiple prior increments.
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var targetExpression = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "org-1", text: null))),
            new SearchParameterExpression(nameParam, new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"))),
        ]);
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, targetExpression);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = (await Resolve.RunAsync(chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- the ordinary predicate (name) lowers first as an ordinary ParamSource, then the
        // resource-column predicate (_id) becomes a filtered ResourceSource, Intersected together
        // (ResourceSource left, ordinary match right) before feeding ChainJoin's InnerMatch.
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text LIKE @p0 (StartsWith) collate CI_AI\n" +
            "cte1 = ResourceSource[105] WHERE ResourceId = @p2\n" +
            "cte2 = Intersect(cte1, cte0)\n" +
            "root = ChainJoin(cte2, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldNotContain("org-1");
        emitted.Sql.ShouldNotContain("Acme");
    }

    [Fact]
    public async Task GivenAForwardChainCombinedWithAnOrdinaryPredicateAndResourceColumnOnTheOuterQuery_WhenCompiled_ThenComposesAllThreeMechanisms()
    {
        // Arrange -- Patient?_id=pt-1&active=true&organization.name=Acme
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false,
            new SearchParameterExpression(nameParam, new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"))));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "pt-1", text: null))),
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null)),
            chain,
        ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- _id is extracted to the outer WHERE (top-level mechanism, unchanged); active and
        // the chain intersect into the match CTE. Explain() pins the exact shape (a task review found
        // the ShouldContain checks below, on their own, would pass identically whether `active` was
        // correctly compiled or silently dropped from the AND entirely -- the exact "predicate
        // silently dropped" bug class this codebase has hit repeatedly. The Explain() assertion is
        // what actually proves all three mechanisms survived into the plan, not just that compilation
        // didn't throw.)
        plan.Explain().ShouldBe(
            "cte0 = TokenSearchParam[103,44]  Code = @p0\n" +
            "cte1 = StringSearchParam[105,202]  Text LIKE @p1 (StartsWith) collate CI_AI\n" +
            "cte2 = ChainJoin(cte1, ref=55, inner=105, output=[103], Forward)\n" +
            "root = Intersect(cte0, cte2) WHERE ResourceId = @p2");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        plan.OuterPredicate.ShouldNotBeNull();
        emitted.Sql.ShouldNotContain("pt-1");
        emitted.Sql.ShouldNotContain("true");
        emitted.Sql.ShouldNotContain("Acme");
    }

    [Fact]
    public async Task GivenPatientIncludeOrganization_WhenCompiledEndToEnd_ThenTheIncludeStageIsForwardWithTheReferencingSideAsTheSeed()
    {
        // Arrange -- Patient?name=Smith&_include=Patient:organization
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = (await Resolve.RunAsync(predicate, includes: [include], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(predicate, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null, top: 50).Plan;

        // Assert -- structure via Explain(), full SQL text via Emit for the whole shape. No modifier on
        // `name` means StringLoweringRule's default arm applies (StartsWith, CI_AI), same as every other
        // unmodified `name` predicate in this file (e.g. GivenAForwardChainQuery above) -- not a plain Equal.
        plan.Explain().ShouldBe(
            "root = StringSearchParam[103,202]  Text LIKE @p0 (StartsWith) collate CI_AI top 50\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)");

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("cteMatchPage AS (");
        emitted.Sql.ShouldContain("SELECT DISTINCT TOP (1001) r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1");
        emitted.Sql.ShouldContain("SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, T1 ASC, Sid1 ASC");
    }

    [Fact]
    public async Task GivenPatientRevincludeObservationSubject_WhenCompiledEndToEnd_ThenTheIncludeStageIsReverseWithTheTranslatedSideAsTheSeed()
    {
        // Arrange -- Patient?name=Smith&_revinclude=Observation:subject
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var subjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"), targetResourceTypes: ["Patient", "Group"]);
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var revInclude = new IncludeExpression(["Observation"], subjectParam, "Observation", "Patient", null, wildCard: false, reversed: true, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 77;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbols = (await Resolve.RunAsync(predicate, includes: [], revIncludes: [revInclude], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [revInclude], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- same StringLoweringRule default-arm shape as the forward-include test above.
        plan.Explain().ShouldBe(
            "root = StringSearchParam[103,202]  Text LIKE @p0 (StartsWith) collate CI_AI\n" +
            "inc0 = IncludeStage(ref=77, seedTypes=[103], outputTypes=[104], seeds=[match], limit=1000, Reverse)");

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("SELECT DISTINCT TOP (1001) rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1");
        emitted.Sql.ShouldContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId");
    }

    [Fact]
    public async Task GivenAnIncludeOnlySearchWithNoOtherFilter_WhenCompiledEndToEnd_ThenTheMatchIsAPlainResourceSource()
    {
        // Arrange -- Patient?_include=Patient:organization, no other search parameter.
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = (await Resolve.RunAsync(expression: null, includes: [include], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null, top: 50).Plan;

        // Assert
        plan.Explain().ShouldBe(
            "root = ResourceSource[103] top 50\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)");

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("cteMatchPage AS (\n    SELECT TOP (50) m.T1, m.Sid1\n    FROM cte0 m\n    ORDER BY m.T1 ASC, m.Sid1 ASC\n)");
    }

    [Fact]
    public async Task GivenPatientIncludeWildcard_WhenCompiledEndToEnd_ThenNoSearchParamIdFilterButOutputTypesAreTheRealReferencedTypes()
    {
        // Arrange -- Patient?_include=Patient:* -- WildCard=true, ReferenceSearchParameter=null,
        // ReferencedTypes carries the REAL resolved output types (design §1.2: this is NOT the "*"
        // sentinel case -- that only arises for _revinclude's wildcard-SOURCE form, tested separately).
        var include = new IncludeExpression(["Patient"], null, "Patient", null, ["Organization", "Practitioner"], wildCard: true, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;
        resolver.ResourceTypeIds["Practitioner"] = 107;

        // Act
        var symbols = (await Resolve.RunAsync(expression: null, includes: [include], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Includes![0].ReferenceSearchParamId.ShouldBeNull();
        plan.Includes[0].OutputTypeIds.ShouldBe([(short)105, (short)107]);

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldNotContain("rsp.SearchParamId");
        emitted.Sql.ShouldContain("(r.ResourceTypeId = 105 OR r.ResourceTypeId = 107)");
    }

    [Fact]
    public async Task GivenRevincludeWildcardSource_WhenCompiledEndToEnd_ThenOutputTypeIdsIsNullSoNoOutputFilterIsEmitted()
    {
        // Arrange -- Patient?_revinclude=*:* -- Produces=["*"] (the literal sentinel, design §1.2).
        var revInclude = new IncludeExpression(["*"], null, "*", "Patient", ["Observation", "Condition"], wildCard: true, reversed: true, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.ResourceTypeIds["Condition"] = 106;

        // Act
        var symbols = (await Resolve.RunAsync(expression: null, includes: [], revIncludes: [revInclude], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [revInclude], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Includes![0].ReferenceSearchParamId.ShouldBeNull();
        plan.Includes[0].OutputTypeIds.ShouldBeNull();
        plan.Includes[0].SeedTypeIds.ShouldBe([(short)103]);

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldNotContain("rsp.SearchParamId");
        emitted.Sql.ShouldNotContain("rsp.ResourceTypeId = 104");
        emitted.Sql.ShouldNotContain("rsp.ResourceTypeId = 106");
    }

    [Fact]
    public async Task GivenChainedIterateIncludesSpecifiedOutOfOrder_WhenCompiledEndToEnd_ThenTheKahnSortReordersThemRegardlessOfInputOrder()
    {
        // Arrange -- Patient?_include=Patient:organization&_include:iterate=Organization:partOf,
        // with the iterate expression listed FIRST in the includes list.
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var partOfParam = new SearchParameterInfo(
            "partof", "partof", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"), targetResourceTypes: ["Organization"]);
        var nonIterate = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var iterate = new IncludeExpression(["Organization"], partOfParam, "Organization", "Organization", null, wildCard: false, reversed: false, iterate: true);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[partOfParam.Url!.ToString()] = 66;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = (await Resolve.RunAsync(expression: null, includes: [iterate, nonIterate], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate, nonIterate], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- non-iterate always sorts first regardless of its position in the input list.
        plan.Explain().ShouldBe(
            "root = ResourceSource[103]\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)\n" +
            "inc1 = IncludeStage(ref=66, seedTypes=[105], outputTypes=[105], seeds=[inc0], limit=1000 iterate, Forward)");

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("SELECT 1 FROM inc0lim m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
    }

    [Fact]
    public async Task GivenAWildcardPatientCompartmentSearch_WhenCompiledEndToEnd_ThenTheCteIsAUnionOfGroupedCompartmentSources()
    {
        // Arrange -- GET /Patient/123/* -- Patient compartment covers Observation (via "subject") and
        // Condition (via "subject" AND "asserter", two distinct membership parameters).
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var asserterParam = new SearchParameterInfo("asserter", "asserter", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Condition-asserter"));
        var compartment = new CompartmentSearchExpression("Patient", "123");

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[asserterParam.Url!.ToString()] = 66;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.ResourceTypeIds["Condition"] = 106;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation", "Condition"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];
        compartmentManager.SearchParams[("Condition", CompartmentType.Patient)] = ["subject", "asserter"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;
        searchParamManager.Parameters[("Condition", "subject")] = subjectParam;
        searchParamManager.Parameters[("Condition", "asserter")] = asserterParam;

        // Act
        var symbols = (await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None,
            compartmentManager, searchParamManager)).Symbols;
        var plan = Lower.Run(compartment, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- inc-free, two grouped CompartmentSource CTEs (one per distinct SearchParamId), Unioned.
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.CompartmentSource>();
        plan.Ctes[1].ShouldBeOfType<CteDefinition.CompartmentSource>();
        plan.Ctes[2].ShouldBeOfType<CteDefinition.Union>();
        plan.Match.ShouldBe(new CteRef(2));

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("SearchParamId = 55");
        emitted.Sql.ShouldContain("SearchParamId = 66");
        emitted.Sql.ShouldContain("(ResourceTypeId = 104 OR ResourceTypeId = 106)"); // subject: Observation + Condition
        emitted.Sql.ShouldContain("ResourceTypeId = 106\n"); // asserter: Condition only, bare Equal
    }

    [Fact]
    public async Task GivenANonWildcardPatientCompartmentSearchForObservation_WhenCompiledEndToEnd_ThenTargetResourceTypeIsUsedNormally()
    {
        // Arrange -- GET /Patient/123/Observation -- FilteredResourceTypes = {"Observation"}, a real
        // targetResourceType ("Observation") is supplied (matching SearchCompartmentHandler's own
        // non-wildcard behavior -- SearchOptions.ResourceType is only ever nulled for "*"). Even a
        // single membership parameter still gets Unioned -- StructuralContext.LowerCompartment always
        // wraps its CompartmentSource group(s) in a Union, unconditionally (design doc §4: "still a
        // Union of N single-type-list CompartmentSource CTEs" -- N=1 included, no shortcut for a
        // single group), so this is 2 CTEs, not a bare CompartmentSource.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbols = (await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation", CancellationToken.None,
            compartmentManager, searchParamManager)).Symbols;
        var plan = Lower.Run(compartment, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Ctes.Count.ShouldBe(2);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.CompartmentSource>();
        plan.Ctes[1].ShouldBeOfType<CteDefinition.Union>();
        plan.Match.ShouldBe(new CteRef(1));

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("ResourceTypeId = 104\n");
        emitted.Sql.ShouldNotContain("(ResourceTypeId = 104)");
    }

    [Fact]
    public async Task GivenANonWildcardCompartmentSearchCombinedWithAnOrdinaryPredicate_WhenCompiledEndToEnd_ThenAnIntersectComposesThem()
    {
        // Arrange -- GET /Patient/123/Observation?category=laboratory -- zero new mechanism (design §4):
        // LowerAnd's existing recursion produces Intersect(compartmentUnion, categoryCte). The compartment
        // side is itself CompartmentSource -> Union (see the single-group note above), so the compartment's
        // own CteRef feeding the outer Intersect is the Union CTE, not the CompartmentSource directly.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var categoryParam = new SearchParameterInfo("category", "category", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-category"));
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });
        var categoryPredicate = new SearchParameterPredicateExpression(categoryParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "laboratory", text: null));
        var tree = new MultiaryExpression(MultiaryOperator.And, [compartment, categoryPredicate]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[categoryParam.Url!.ToString()] = 22;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbols = (await Resolve.RunAsync(
            tree, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation", CancellationToken.None,
            compartmentManager, searchParamManager)).Symbols;
        var plan = Lower.Run(tree, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Ctes.Count.ShouldBe(4);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.CompartmentSource>();
        plan.Ctes[1].ShouldBeOfType<CteDefinition.Union>();
        plan.Ctes[2].ShouldBeOfType<CteDefinition.ParamSource>();
        plan.Ctes[3].ShouldBeOfType<CteDefinition.Intersect>();
        plan.Match.ShouldBe(new CteRef(3));
    }

    [Fact]
    public async Task GivenACompartmentSearchThatResolvesToZeroMembershipParameters_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- GET /Patient/123/NotInCompartment (design §2's degenerate case).
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "NotInCompartment" });

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"]; // "NotInCompartment" isn't listed

        var searchParamManager = new FakeSearchParameterDefinitionManager();

        // Act
        var symbols = (await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None,
            compartmentManager, searchParamManager)).Symbols;

        // Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(compartment, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("zero membership");
    }

    [Fact]
    public async Task GivenAPatientSearchSortedByName_WhenCompiledEndToEnd_ThenTheMatchGainsAnIsMinJoinAndAnOrderBy()
    {
        // Arrange -- Patient?name=Smith&_sort=name, first page.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbols = (await Resolve.RunAsync(
            predicate, includes: [], revIncludes: [], sort: [new SortExpression(nameParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null, top: 10).Plan;

        // Assert
        plan.Explain().ShouldContain("sort = SortSpec([String:202 ASC], Valued)");
        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("sk0.IsMin = 1");
        emitted.Sql.ShouldContain("ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public async Task GivenAPatientSearchSortedByNameWithAnInclude_WhenCompiledEndToEnd_ThenIncludeStageMachineryIsUnchangedAndTheOuterUnionCarriesTheSortValue()
    {
        // Arrange -- Patient?_sort=name&_include=Patient:organization, proving §4's "IncludeStage
        // needs zero changes" composability claim through the real pipeline, not just Emit in isolation.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = (await Resolve.RunAsync(
            expression: null, includes: [include], revIncludes: [], sort: [new SortExpression(nameParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(
            expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000,
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null, top: 50).Plan;

        // Assert -- IncludeStage's own fields are exactly what Phase 7 already produces; no new field.
        plan.Includes!.Count.ShouldBe(1);
        plan.Includes[0].SeedFromMatch.ShouldBeTrue();
        plan.Includes[0].SeedStages.ShouldBeEmpty();

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("cteMatchPage AS (");
        emitted.Sql.ShouldContain("sk0.IsMin = 1");
        emitted.Sql.ShouldContain(
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, SortValue0 FROM cteMatchPage");
        emitted.Sql.ShouldContain("SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial, NULL FROM inc0lim i");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, SortValue0 ASC, T1 ASC, Sid1 ASC");
    }

    [Fact]
    public async Task GivenAPatientSearchSortedByNameAscendingThenBirthdateDescending_WhenCompiledEndToEnd_ThenBothKeysAppearWithTheCorrectJoinTypesAndDirections()
    {
        // Arrange -- Patient?_sort=name,-birthdate.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var birthDateParam = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[birthDateParam.Url!.ToString()] = 303;
        resolver.ResourceTypeIds["Patient"] = 103;

        var sortExpressions = new List<SortExpression>
        {
            new(nameParam, SortOrder.Ascending),
            new(birthDateParam, SortOrder.Descending),
        };

        // Act
        var symbols = (await Resolve.RunAsync(
            expression: null, includes: [], revIncludes: [], sort: sortExpressions, resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(
            expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: sortExpressions, sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Sort!.Keys.Count.ShouldBe(2);
        plan.Sort.Keys[0].Kind.ShouldBe(SortKeyKind.String);
        plan.Sort.Keys[1].Kind.ShouldBe(SortKeyKind.Date);
        plan.Sort.Keys[1].Direction.ShouldBe(SortOrder.Descending);

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain("sk0.IsMin = 1");
        emitted.Sql.ShouldContain("LEFT JOIN dbo.DateTimeSearchParam sk1");
        emitted.Sql.ShouldContain("sk1.IsMax = 1");
        emitted.Sql.ShouldContain("ORDER BY sk0.Text ASC, ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') DESC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public async Task GivenACompartmentSearchSortedByName_WhenCompiledEndToEnd_ThenTheSortDecorationComposesWithTheCompartmentUnionRoot()
    {
        // Arrange -- GET /Patient/123/Observation?_sort=name -- proves the #5672-class fhir-server bug
        // (SMART compartment + _sort by a parameter returning empty results) does not apply here: a
        // compartment match root is just another Union CteRef, sort-agnostic, composed for free.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Observation-name"));
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbols = (await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [new SortExpression(nameParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Observation", CancellationToken.None, compartmentManager, searchParamManager)).Symbols;
        var plan = Lower.Run(
            compartment, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- the match is the compartment's own Union; sort still decorates cleanly on top.
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("sk0.IsMin = 1");
        emitted.Sql.ShouldContain("ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public async Task GivenTheMissingPrimaryPhaseWithAPageBoundary_WhenCompiledEndToEnd_ThenTheSeekPredicateIsSidOnly()
    {
        // Arrange -- Patient?_sort=name, second (missing-name) phase, resuming after a prior page.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbols = (await Resolve.RunAsync(
            expression: null, includes: [], revIncludes: [], sort: [new SortExpression(nameParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var page = new PageSpec([], new SqlParameterRef((short)103), new SqlParameterRef(7000L));
        var plan = Lower.Run(
            expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.MissingPrimary, page: page, top: 10).Plan;

        // Assert -- ResourceSource's own ResourceTypeId is itself a bound parameter (@p0, same accounting
        // already established by GivenAPatientIdOnlyQuery above), so the seek predicate's own two
        // boundary parameters are @p1/@p2, not @p0/@p1. The NOT EXISTS filter and the 2-branch seek
        // predicate must be ANDed together with the OR chain parenthesized as a single unit -- otherwise
        // NOT EXISTS binds only to the first branch (T-SQL's AND-over-OR precedence) and the second
        // branch silently bypasses the missing-name filter on page 2+.
        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldNotContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain(
            "WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202) " +
            "AND ((m.T1 = @p1 AND m.Sid1 > @p2)\n" +
            "       OR (m.T1 > @p1))");
    }

    [Fact]
    public async Task GivenACompartmentSearchWithCountOnly_WhenCompiledEndToEnd_ThenTheCountQueryReusesTheCompartmentUnionRoot()
    {
        // Arrange -- GET /Patient/123/Observation?_total=accurate -- proves CountOnly composes with the
        // Union-rooted compartment match graph, including the DISTINCT that matters for a Union root.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbols = (await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation",
            CancellationToken.None, compartmentManager, searchParamManager)).Symbols;
        var plan = Lower.Run(
            compartment, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, countOnly: true).Plan;

        // Assert
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1)");
        emitted.Sql.ShouldNotContain("TOP (");
        emitted.Sql.ShouldNotContain("ORDER BY");
    }

    [Fact]
    public async Task GivenAChainWithMissingInsideTheTargetExpression_WhenCompiledEndToEnd_ThenTheMissingBranchIsReachableAtChainNestingDepth()
    {
        // Arrange -- Patient?organization.name:missing=true -- the referenced Organization has no name.
        var orgRefParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var missingName = new MissingSearchParameterExpression(nameParam, isMissing: true);
        var chain = new ChainedExpression(["Patient"], orgRefParam, ["Organization"], reversed: false, missingName);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgRefParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = (await Resolve.RunAsync(
            chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(
            chain, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- structural, not SQL-text, assertions (matching Task 3's own :missing=true test style):
        // the chain's InnerMatch CteRef must point at the Except/ResourceSource/ParamSource shape
        // LowerMissing produces standalone, proving it is reachable at chain-nesting depth with zero
        // new chain-specific wiring. The plan's match root itself is the ChainJoin.
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.ChainJoin>();
        var chainJoin = (CteDefinition.ChainJoin)plan.Ctes[plan.Match.Index];
        plan.Ctes[chainJoin.InnerMatch.Index].ShouldBeOfType<CteDefinition.Except>();
        var except = (CteDefinition.Except)plan.Ctes[chainJoin.InnerMatch.Index];
        plan.Ctes[except.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.Ctes[except.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>();
        ((CteDefinition.ParamSource)plan.Ctes[except.Right.Index]).Predicate.ShouldBeNull();

        // Also confirm the whole plan still emits without error -- a real, if not exhaustively
        // asserted, proof that ChainJoin's Emit code and the Except/ParamSource-no-predicate shape
        // compose into valid SQL text end to end.
        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GivenAPatientMissingNameQueryWithAnInclude_WhenCompiledEndToEnd_ThenTheIncludeMachineryWrapsTheExceptRootedMatch()
    {
        // Arrange -- Patient?name:missing=true&_include=Patient:organization -- proves the includes
        // path (cteMatchPage/UNION ALL) correctly wraps an Except-rooted match, not just the
        // ParamSource-rooted match every other include test in this file exercises.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var missingName = new MissingSearchParameterExpression(nameParam, isMissing: true);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = (await Resolve.RunAsync(
            missingName, includes: [include], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(
            missingName, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000,
            sort: [], sortPhase: SortPhase.Valued, page: null, top: 50).Plan;

        // Assert -- the match CTE is genuinely the Except/ResourceSource/ParamSource shape LowerMissing
        // produces, matching Task 3's own :missing=true structural style, not a ParamSource.
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Except>();
        var except = (CteDefinition.Except)plan.Ctes[plan.Match.Index];
        plan.Ctes[except.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.Ctes[except.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>();
        ((CteDefinition.ParamSource)plan.Ctes[except.Right.Index]).Predicate.ShouldBeNull();

        var emitted = SqlBuilder.Run(plan);

        // Assert -- the includes machinery (cteMatchPage + the final UNION ALL) is present, and
        // cteMatchPage's own FROM clause points at cte{plan.Match.Index} -- the Except CTE -- proving
        // the includes path is agnostic to the match CTE's own internal shape.
        emitted.Sql.ShouldContain(
            $"cteMatchPage AS (\n    SELECT TOP (50) m.T1, m.Sid1\n    FROM {SqlLabels.CteLabel(plan.Match.Index)} m\n    ORDER BY m.T1 ASC, m.Sid1 ASC\n)");
        emitted.Sql.ShouldContain("UNION ALL");
        emitted.Sql.ShouldContain(
            $"    SELECT {SqlLabels.CteLabel(except.Left.Index)}.T1, {SqlLabels.CteLabel(except.Left.Index)}.Sid1\n" +
            $"    FROM {SqlLabels.CteLabel(except.Left.Index)}\n" +
            $"    WHERE NOT EXISTS (\n" +
            $"        SELECT 1 FROM {SqlLabels.CteLabel(except.Right.Index)}\n" +
            $"        WHERE {SqlLabels.CteLabel(except.Right.Index)}.T1 = {SqlLabels.CteLabel(except.Left.Index)}.T1 AND {SqlLabels.CteLabel(except.Right.Index)}.Sid1 = {SqlLabels.CteLabel(except.Left.Index)}.Sid1)");
    }

    [Fact]
    public async Task GivenAPatientMissingNameQuerySortedByLastUpdated_WhenCompiledEndToEnd_ThenTheExceptRootedMatchStillCarriesTheOrderBy()
    {
        // Arrange -- Patient?name:missing=true&_sort=_lastUpdated -- _lastUpdated needs no extra
        // symbol resolution (Lower.BuildSortKey short-circuits it before any SearchParamId lookup),
        // the simplest composition proving :missing's Except-rooted match also sorts correctly.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var missingName = new MissingSearchParameterExpression(nameParam, isMissing: true);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbols = (await Resolve.RunAsync(
            missingName, includes: [], revIncludes: [], sort: [new SortExpression(lastUpdatedParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(
            missingName, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(lastUpdatedParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- the match CTE is still the Except/ResourceSource/ParamSource shape.
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Except>();
        var except = (CteDefinition.Except)plan.Ctes[plan.Match.Index];
        plan.Ctes[except.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.Ctes[except.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>();

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldContain($"SELECT m.T1, m.Sid1, m.Sid1 AS SortValue0 FROM cte{plan.Match.Index} m");
        emitted.Sql.ShouldEndWith("ORDER BY m.Sid1 ASC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public async Task GivenAPatientMissingNameQueryWithCountOnly_WhenCompiledEndToEnd_ThenTheCountQueryTargetsTheExceptRootedMatch()
    {
        // Arrange -- Patient?name:missing=true&_total=accurate -- proves CountOnly's terminal-SELECT
        // swap works regardless of the match CTE's own shape: every other CountOnly test in this file
        // (e.g. GivenACompartmentSearchWithCountOnly above) targets a Union root, not an Except root.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var missingName = new MissingSearchParameterExpression(nameParam, isMissing: true);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbols = (await Resolve.RunAsync(
            missingName, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;
        var plan = Lower.Run(
            missingName, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, countOnly: true).Plan;

        // Assert -- the match CTE is the Except/ResourceSource/ParamSource shape, NOT a bare ParamSource.
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Except>();

        var emitted = SqlBuilder.Run(plan);
        emitted.Sql.ShouldEndWith($"SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte{plan.Match.Index} m");
        emitted.Sql.ShouldNotContain("TOP (");
        emitted.Sql.ShouldNotContain("ORDER BY");
    }

    // ─── Phase 1: Terminology Resolution and Qualified Values ───────────────────

    [Fact]
    public async Task GivenASystemCodeQualifiedTokenQuery_WhenCompiled_ThenPinsSystemIdAndCodePredicatesAndParameters()
    {
        // Arrange -- Observation?code=http://loinc.org|8480-6 (system|code)
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var tree = new SearchParameterPredicateExpression(
            codeParam, SearchComparator.Eq, modifier: null,
            new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null));
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[codeParam.Url!.ToString()] = 88;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.SystemIds["http://loinc.org"] = 7;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- system resolves to surrogate id 7; code remains a string parameter; parameter order is SystemId then Code
        plan.Explain().ShouldBe("root = TokenSearchParam[104,88]  SystemId = @p0 AND Code = @p1");
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 88 AND (SystemId = @p0 AND Code = @p1)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)7), ("@p1", (object)"8480-6")]);
    }

    [Fact]
    public async Task GivenAnEmptySystemCodeTokenQuery_WhenCompiled_ThenSqlContainsSystemIdIsNullAndCodeEqualityWithNoSystemParameter()
    {
        // Arrange -- Observation?code=|8480-6 (|code, empty system — SystemId must be NULL in the indexed row)
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var tree = new SearchParameterPredicateExpression(
            codeParam, SearchComparator.Eq, modifier: null,
            new TokenSearchValue(system: "", code: "8480-6", text: null));
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[codeParam.Url!.ToString()] = 88;
        resolver.ResourceTypeIds["Observation"] = 104;
        // No system ID configured: empty string is never looked up

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- IS NULL guard with no system-id parameter; code is the sole bound parameter
        plan.Explain().ShouldBe("root = TokenSearchParam[104,88]  SystemId IS NULL AND Code = @p0");
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 88 AND (SystemId IS NULL AND Code = @p0)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)"8480-6")]);
    }

    [Fact]
    public async Task GivenAQuantityWithSystemAndCodeQuery_WhenCompiled_ThenPinsNumericRangePlusSystemIdAndQuantityCodeIdParameters()
    {
        // Arrange -- Observation?value-quantity=ge107|http://unitsofmeasure.org|mg
        var quantityParam = new SearchParameterInfo("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-quantity"));
        var tree = new SearchParameterPredicateExpression(
            quantityParam, SearchComparator.Ge, modifier: null,
            new QuantitySearchValue("http://unitsofmeasure.org", "mg", 107m));
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[quantityParam.Url!.ToString()] = 204;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.SystemIds["http://unitsofmeasure.org"] = 11;
        resolver.QuantityCodeIds["mg"] = 22;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- Ge: raw value used (no precision-widening bounds); SystemId and QuantityCodeId appended in that order
        plan.Explain().ShouldBe("root = QuantitySearchParam[104,204]  LowValue >= @p0 AND SystemId = @p1 AND QuantityCodeId = @p2");
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.QuantitySearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 204 AND ((LowValue >= @p0 AND SystemId = @p1) AND QuantityCodeId = @p2)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)107m), ("@p1", (object)11), ("@p2", (object)22)]);
    }

    [Fact]
    public async Task GivenASystemQualifiedTokenTokenCompositeQuery_WhenCompiled_ThenPinsSystemIdInSlot1AndCodeForBothSlots()
    {
        // Arrange -- Observation?code-value-concept=http://loinc.org|8480-6$high
        var compositeParam = new SearchParameterInfo(
            "code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var valueParam = new SearchParameterInfo("value-concept", "value-concept", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-concept"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null,
                        new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null))),
                new CompositeComponentExpression(valueParam, 1,
                    new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null,
                        new TokenSearchValue(system: null, code: "high", text: null))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 301;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.SystemIds["http://loinc.org"] = 7;

        // Act
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- slot 1 carries SystemId1 (integer) then Code1; slot 2 carries Code2 only (null system → no constraint)
        plan.Explain().ShouldBe("root = TokenTokenCompositeSearchParam[104,301]  SystemId1 = @p0 AND Code1 = @p1 AND Code2 = @p2");
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenTokenCompositeSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 301 AND ((SystemId1 = @p0 AND Code1 = @p1) AND Code2 = @p2)\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)7), ("@p1", (object)"8480-6"), ("@p2", (object)"high")]);
    }

    [Fact]
    public async Task GivenAnUnknownSystemTokenQuery_WhenCompiled_ThenResolveSucceedsAndEmittedSqlContains1Equals0()
    {
        // Arrange -- Observation?code=http://unknown.org|abc; resolver knows nothing about this system
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var tree = new SearchParameterPredicateExpression(
            codeParam, SearchComparator.Eq, modifier: null,
            new TokenSearchValue(system: "http://unknown.org", code: "abc", text: null));
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[codeParam.Url!.ToString()] = 88;
        resolver.ResourceTypeIds["Observation"] = 104;
        // resolver.SystemIds intentionally has no entry for "http://unknown.org" — GetSystemIdAsync returns null

        // Act -- Resolve must not throw; the known-miss is stored; Lower lowers to Predicate.False; Emit must not throw
        var symbolTable = (await Resolve.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;
        var emitted = SqlBuilder.Run(plan);

        // Assert -- false predicate in plan; 1 = 0 in SQL; no user value exposed; no bound parameters
        plan.Explain().ShouldBe("root = TokenSearchParam[104,88]  false");
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 104 AND SearchParamId = 88 AND 1 = 0\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.ShouldBeEmpty();
    }

    private sealed class FakeCompartmentDefinitionManager : ICompartmentDefinitionManager
    {
        public Dictionary<CompartmentType, HashSet<string>> ResourceTypes { get; } = [];

        public Dictionary<(string ResourceType, CompartmentType CompartmentType), HashSet<string>> SearchParams { get; } = [];

        public bool TryGetResourceTypes(CompartmentType compartmentType, out HashSet<string> resourceTypes)
            => ResourceTypes.TryGetValue(compartmentType, out resourceTypes!);

        public bool TryGetSearchParams(string resourceType, CompartmentType compartmentType, out HashSet<string> searchParams)
            => SearchParams.TryGetValue((resourceType, compartmentType), out searchParams!);
    }

    private sealed class FakeSearchParameterDefinitionManager : ISearchParameterDefinitionManager
    {
        public Dictionary<(string ResourceType, string Code), SearchParameterInfo> Parameters { get; } = [];

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
            => Parameters.TryGetValue((resourceType, code), out searchParameter!);

        public IEnumerable<SearchParameterInfo> AllSearchParameters => throw new NotImplementedException();
        public IReadOnlyDictionary<string, string> SearchParameterHashMap => throw new NotImplementedException();
        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType) => throw new NotImplementedException();
        public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters) => throw new NotImplementedException();
        public SearchParameterInfo GetSearchParameter(string resourceType, string code) => throw new NotImplementedException();
        public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value) => throw new NotImplementedException();
        public SearchParameterInfo GetSearchParameter(Uri definitionUri) => throw new NotImplementedException();
        public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap) => throw new NotImplementedException();
        public string GetSearchParameterHashForResourceType(string resourceType) => throw new NotImplementedException();
        public void AddNewSearchParameters(IReadOnlyCollection<Ignixa.Abstractions.IElement> searchParameters, bool calculateHash = true) => throw new NotImplementedException();
        public void DeleteSearchParameter(string url, bool calculateHash = true) => throw new NotImplementedException();
    }
}
