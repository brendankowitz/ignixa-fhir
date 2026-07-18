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
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, top: 10);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], resolver, "ValueSet", CancellationToken.None);
        var plan = Lower.Run(predicate, symbolTable, targetResourceType: "ValueSet", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = UriSearchParam[105,88]  Uri = @p0");
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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Observation", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "ValueSet", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "ValueSet", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

        // Assert -- identical plan shape to the bare-predicate case above (same table, same SearchParamId)
        plan.Explain().ShouldBe("root = UriSearchParam[105,88]  Uri = @p0");
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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Observation", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Observation", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Observation", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Observation", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Observation", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Observation", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "DocumentReference", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "DocumentReference", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe(
            "root = ReferenceTokenCompositeSearchParam[55,404]  ReferenceResourceTypeId1 = @p0 AND ReferenceResourceId1 = @p1 AND Code2 = @p2");
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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        Should.Throw<NotSupportedException>(() => Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0));
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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(chain, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(chain, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(outerChain, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(outerChain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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

        Should.Throw<NotSupportedException>(() => Lower.Run(current, symbolTable, targetResourceType: "Organization", includes: [], revIncludes: [], includeLimit: 0))
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
        var symbolTable = await Resolve.RunAsync(chain, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(chain, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(chain, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(chain, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
        var symbolTable = await Resolve.RunAsync(tree, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
        var emitted = Emit.Run(plan);

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
}
