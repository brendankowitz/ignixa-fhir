using Ignixa.Search.Definition;
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
        var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(parameter).ShouldBe((short)202);
    }

    [Fact]
    public async Task GivenACompositeTree_WhenResolved_ThenTheCompositeAndBothComponentsAreResolved()
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
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"] = 400;

        // Act
        var symbolTable = await Resolve.RunAsync(composite, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(compositeParam).ShouldBe((short)400);
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
        var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None);

        // Assert -- the miss only surfaces when something actually looks the parameter up later.
        Should.Throw<KeyNotFoundException>(() => symbolTable.SearchParamId(parameter));
    }

    [Fact]
    public async Task GivenATreeWithAReferencePredicate_WhenResolved_ThenSymbolTableHasItsResourceTypeId()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: "123"));
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[parameter.Url.ToString()] = 77;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None);

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
    }

    [Fact]
    public async Task GivenATypePredicate_WhenResolved_ThenSymbolTableHasItsOwnValuesResourceTypeId()
    {
        // Arrange -- _type=Observation, where the query's own targetResourceType is a DIFFERENT type
        // ("Patient") than the _type value being searched for -- a non-tautological case. Without
        // collecting _type's own TokenSearchValue.Code, ResourceColumnLoweringRule.TypeEquals would
        // find no "Observation" entry in the SymbolTable and throw KeyNotFoundException.
        var typeParam = new SearchParameterInfo("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));
        var predicate = new SearchParameterPredicateExpression(typeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "Observation", text: null));
        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None);

        // Assert
        symbolTable.ResourceTypeId("Observation").ShouldBe((short)104);
    }

    [Fact]
    public async Task GivenATargetResourceType_WhenResolved_ThenSymbolTableHasItsResourceTypeIdEvenWithNoReferenceInTheTree()
    {
        // Arrange -- a plain String predicate, nothing in the tree itself mentions "Patient"
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[parameter.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None);

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
    }

    [Fact]
    public async Task GivenAChainedExpression_WhenResolved_ThenSymbolTableHasTheReferenceParamAndBothResourceTypes()
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
        var symbolTable = await Resolve.RunAsync(chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(orgParam).ShouldBe((short)55);
        symbolTable.SearchParamId(nameParam).ShouldBe((short)202);
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        symbolTable.ResourceTypeId("Organization").ShouldBe((short)105);
    }

    [Fact]
    public async Task GivenAForwardIncludeExpression_WhenResolved_ThenSymbolTableHasItsReferenceParamAndBothResourceTypes()
    {
        // Arrange -- Patient?_include=Patient:organization
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: orgParam,
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: null,
            wildCard: false,
            reversed: false,
            iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = await Resolve.RunAsync(
            expression: null, includes: [include], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(orgParam).ShouldBe((short)55);
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        symbolTable.ResourceTypeId("Organization").ShouldBe((short)105);
    }

    [Fact]
    public async Task GivenARevincludeWildcardSourceExpression_WhenResolved_ThenTheStarSentinelIsNeverPassedToTheResolver()
    {
        // Arrange -- Patient?_revinclude=*:* -- SourceResourceType is the literal sentinel "*"
        // (design doc §1.2); CollectInclude must skip it, not call GetResourceTypeIdAsync("*").
        var include = new IncludeExpression(
            resourceTypes: ["*"],
            referenceSearchParameter: null,
            sourceResourceType: "*",
            targetResourceType: "Patient",
            referencedTypes: ["Observation", "Condition"],
            wildCard: true,
            reversed: true,
            iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.ResourceTypeIds["Condition"] = 106;

        // Act -- must not throw even though the resolver has no row for "*"
        var symbolTable = await Resolve.RunAsync(
            expression: null, includes: [], revIncludes: [include], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        symbolTable.ResourceTypeId("Observation").ShouldBe((short)104);
        symbolTable.ResourceTypeId("Condition").ShouldBe((short)106);
        Should.Throw<KeyNotFoundException>(() => symbolTable.ResourceTypeId("*"));
    }

    [Fact]
    public async Task GivenACompartmentSearchExpression_WhenResolved_ThenSymbolTableHasItsCompartmentMembership()
    {
        // Arrange -- Patient/123/Observation-shaped: Patient compartment, Observation membership via "subject".
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[Ignixa.Specification.ValueSets.Normative.CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", Ignixa.Specification.ValueSets.Normative.CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 77;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation", CancellationToken.None,
            compartmentManager, searchParamManager);

        // Assert
        var membership = symbolTable.CompartmentMembership("Patient");
        membership.Count.ShouldBe(1);
        membership[0].Parameter.ShouldBe(subjectParam);
        membership[0].ResourceTypes.ShouldBe(["Observation"]);
        symbolTable.SearchParamId(subjectParam).ShouldBe((short)77);
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        symbolTable.ResourceTypeId("Observation").ShouldBe((short)104);
    }

    [Fact]
    public async Task GivenACompartmentSearchExpressionWithNoManagersSupplied_WhenResolved_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var resolver = new FakeSymbolResolver();

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() =>
            Resolve.RunAsync(compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation", CancellationToken.None));
    }

    [Fact]
    public async Task GivenASortExpression_WhenResolved_ThenSymbolTableHasItsSearchParamId()
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var sortExpression = new SortExpression(nameParam, SortOrder.Ascending);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(
            predicate, includes: [], revIncludes: [], sort: [sortExpression], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(nameParam).ShouldBe((short)202);
    }

    [Fact]
    public async Task GivenALastUpdatedSortExpression_WhenResolved_ThenNoSearchParamIdIsRequested()
    {
        // Arrange -- _lastUpdated needs no SearchParamId lookup at all.
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var sortExpression = new SortExpression(lastUpdatedParam, SortOrder.Descending);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act -- must not throw even though the resolver has no SearchParamId row for _lastUpdated.
        var symbolTable = await Resolve.RunAsync(
            expression: null, includes: [], revIncludes: [], sort: [sortExpression], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        Should.Throw<KeyNotFoundException>(() => symbolTable.SearchParamId(lastUpdatedParam));
    }

    /// <summary>
    /// An in-memory, dictionary-backed ICompartmentDefinitionManager test double -- not a mock,
    /// matching this file's existing FakeSymbolResolver philosophy.
    /// </summary>
    private sealed class FakeCompartmentDefinitionManager : ICompartmentDefinitionManager
    {
        public Dictionary<Ignixa.Specification.ValueSets.Normative.CompartmentType, HashSet<string>> ResourceTypes { get; } = [];

        public Dictionary<(string ResourceType, Ignixa.Specification.ValueSets.Normative.CompartmentType CompartmentType), HashSet<string>> SearchParams { get; } = [];

        public bool TryGetResourceTypes(Ignixa.Specification.ValueSets.Normative.CompartmentType compartmentType, out HashSet<string> resourceTypes)
            => ResourceTypes.TryGetValue(compartmentType, out resourceTypes!);

        public bool TryGetSearchParams(string resourceType, Ignixa.Specification.ValueSets.Normative.CompartmentType compartmentType, out HashSet<string> searchParams)
            => SearchParams.TryGetValue((resourceType, compartmentType), out searchParams!);
    }

    /// <summary>
    /// A minimal ISearchParameterDefinitionManager test double implementing only what Resolve calls
    /// (TryGetSearchParameter) -- every other member throws NotImplementedException deliberately,
    /// surfacing loudly if a future change makes Resolve call something this test double doesn't expect.
    /// </summary>
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
