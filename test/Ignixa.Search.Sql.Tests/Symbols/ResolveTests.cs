using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.TestSupport;
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
        var symbolTable = (await ResolveHarness.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(composite, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
    }

    [Fact]
    public async Task GivenAnUntypedReferenceWithADeclaredTargetTheResolverCannotFind_WhenResolved_ThenSymbolTableStoresTheUnmatchableSentinel()
    {
        // Arrange -- Patient?organization=org-123 where "Organization" is a declared target type but
        // the resolver has no row for it. SymbolCollectingVisitor adds "Organization" to the resource-
        // type set (untyped-reference branch); Resolve must then store UnmatchableResourceTypeId (-1)
        // rather than omitting the key, so DeclaredTargetResourceTypeIds finds it and includes the
        // sentinel in the OR list instead of collapsing to an unconstrained match.
        var parameter = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: null, resourceId: "org-123"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[parameter.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        // "Organization" is deliberately absent from resolver.ResourceTypeIds -- GetResourceTypeIdAsync returns null

        // Act
        var symbolTable = (await ResolveHarness.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;

        // Assert -- Resolve stored the sentinel rather than omitting the key; the entry is present as -1.
        symbolTable.ResourceTypeId("Organization").ShouldBe(SymbolTable.UnmatchableResourceTypeId);
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
        var symbolTable = (await ResolveHarness.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(
            expression: null, includes: [include], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(
            expression: null, includes: [], revIncludes: [include], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation", CancellationToken.None,
            compartmentManager, searchParamManager)).Symbols;

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
    public async Task GivenACompartmentMemberParameterWithNoUrl_WhenResolved_ThenItIsSkippedRatherThanThrowing()
    {
        // Arrange -- SearchParameterInfo's Url is typed non-null but nullable at runtime, and
        // SearchSqlCompiler.RunAsync awaits Resolve outside its try/catch, so a dereference here would
        // escape as an NRE.
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });
        var urllessParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, url: null);

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[Ignixa.Specification.ValueSets.Normative.CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", Ignixa.Specification.ValueSets.Normative.CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = urllessParam;

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var resolved = await ResolveHarness.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation", CancellationToken.None,
            compartmentManager, searchParamManager);

        // Assert
        resolved.Symbols.CompartmentMembership("Patient").ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenACompartmentSearchExpressionWithNoManagersSupplied_WhenResolved_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var resolver = new FakeSymbolResolver();

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() =>
            ResolveHarness.RunAsync(compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation", CancellationToken.None));
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
        var symbolTable = (await ResolveHarness.RunAsync(
            predicate, includes: [], revIncludes: [], sort: [sortExpression], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;

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
        var symbolTable = (await ResolveHarness.RunAsync(
            expression: null, includes: [], revIncludes: [], sort: [sortExpression], resolver, targetResourceType: "Patient", CancellationToken.None)).Symbols;

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        Should.Throw<KeyNotFoundException>(() => symbolTable.SearchParamId(lastUpdatedParam));
    }

    [Fact]
    public async Task GivenATokenQuantityCompositeWithDuplicateSystems_WhenResolved_ThenTerminologyResolvedOnceAndStoredInSymbolTable()
    {
        // Arrange -- mirrors the tree from GivenACompositeTree_WhenResolved_... with terminology IDs;
        // "http://loinc.org" appears in both the composite leaf and a standalone leaf to prove deduplication.
        var codeParam = new SearchParameterInfo(
            "component-code", "component-code", SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code"));
        var quantityParam = new SearchParameterInfo(
            "component-value-quantity", "component-value-quantity", SearchParamType.Quantity,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-value-quantity"));
        var compositeParam = new SearchParameterInfo(
            "component-code-value-quantity", "component-code-value-quantity", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"));

        var codePredicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null,
            new TokenSearchValue("http://loinc.org", "8480-6", text: null));
        var quantityPredicate = new SearchParameterPredicateExpression(quantityParam, SearchComparator.Eq, modifier: null,
            new QuantitySearchValue("http://unitsofmeasure.org", "mg", 107m));

        var codeComponent = new CompositeComponentExpression(codeParam, 0, codePredicate);
        var quantityComponent = new CompositeComponentExpression(quantityParam, 1, quantityPredicate);
        var composite = new SearchParameterExpression(compositeParam,
            new MultiaryExpression(MultiaryOperator.And, [codeComponent, quantityComponent]));

        // A second leaf using the same system -- proves the collector deduplicates before calling the resolver.
        var duplicatePredicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null,
            new TokenSearchValue("http://loinc.org", "8480-7", text: null));
        var duplicateLeaf = new SearchParameterExpression(codeParam, duplicatePredicate);

        var tree = new MultiaryExpression(MultiaryOperator.And, [composite, duplicateLeaf]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-component-code"] = 401;
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-component-value-quantity"] = 402;
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"] = 400;
        resolver.SystemIds["http://loinc.org"] = 7;
        resolver.SystemIds["http://unitsofmeasure.org"] = 8;
        resolver.QuantityCodeIds["mg"] = 42;

        // Act
        var symbolTable = (await ResolveHarness.RunAsync(tree, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;

        // Assert -- all three terminology values are present and resolved
        symbolTable.SystemId("http://loinc.org").ShouldBe(7);
        symbolTable.SystemId("http://unitsofmeasure.org").ShouldBe(8);
        symbolTable.QuantityCodeId("mg").ShouldBe(42);

        // Prove each resolver method was called exactly once per distinct string, not once per occurrence
        resolver.SystemIdCallCounts["http://loinc.org"].ShouldBe(1);
        resolver.SystemIdCallCounts["http://unitsofmeasure.org"].ShouldBe(1);
        resolver.QuantityCodeIdCallCounts["mg"].ShouldBe(1);
    }

    [Fact]
    public async Task GivenASystemThatTheResolverDoesNotKnow_WhenResolved_ThenSymbolTableStoresAKnownMiss()
    {
        // Arrange -- "http://unknown.example" is in the tree but absent from the resolver, so it returns null
        var codeParam = new SearchParameterInfo(
            "code", "code", SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var predicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null,
            new TokenSearchValue("http://unknown.example", "some-code", text: null));
        var expression = new SearchParameterExpression(codeParam, predicate);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-code"] = 88;
        // resolver.SystemIds does NOT contain "http://unknown.example" -- resolver returns null (known miss)

        // Act
        var symbolTable = (await ResolveHarness.RunAsync(expression, includes: [], revIncludes: [], sort: [], resolver, "Observation", CancellationToken.None)).Symbols;

        // Assert -- the system was collected and the resolver's null result was stored as a known miss
        symbolTable.SystemId("http://unknown.example").ShouldBeNull();
        // An uncollected system still throws (three-state invariant)
        Should.Throw<KeyNotFoundException>(() => symbolTable.SystemId("http://never-seen.example"));
    }

    [Fact]
    public async Task GivenANotReferencedPath_WhenResolved_ThenTheReferenceParameterIsResolvedAndStored()
    {
        // Arrange -- Patient?_not-referenced=Observation:subject. Resolve must look the (Observation,
        // subject) pair up through the definition manager, then resolve that parameter's id like any other.
        var subjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var expression = new NotReferencedExpression("Observation", "subject");

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 969;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 96;

        var definitions = new FakeSearchParameterDefinitionManager();
        definitions.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbolTable = (await ResolveHarness.RunAsync(
            expression, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None,
            searchParameterDefinitionManager: definitions)).Symbols;

        // Assert
        var resolvedParam = symbolTable.NotReferencedPath("Observation", "subject");
        resolvedParam.ShouldNotBeNull();
        symbolTable.SearchParamId(resolvedParam).ShouldBe((short)969);
        symbolTable.ResourceTypeId("Observation").ShouldBe((short)96);
    }

    [Fact]
    public async Task GivenANotReferencedPathThatIsNotAReferenceParameter_WhenResolved_ThenItFallsBackToPathAgnostic()
    {
        // Arrange -- a non-reference parameter cannot anchor the anti-join, so Resolve records no path and
        // Lower falls back to source-type-only filtering, matching the shipping engine.
        var statusParam = new SearchParameterInfo(
            "status", "status", SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var expression = new NotReferencedExpression("Observation", "status");

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 96;

        var definitions = new FakeSearchParameterDefinitionManager();
        definitions.Parameters[("Observation", "status")] = statusParam;

        // Act
        var symbolTable = (await ResolveHarness.RunAsync(
            expression, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None,
            searchParameterDefinitionManager: definitions)).Symbols;

        // Assert -- no reference path resolved, but the source type is still available
        symbolTable.NotReferencedPath("Observation", "status").ShouldBeNull();
        symbolTable.ResourceTypeId("Observation").ShouldBe((short)96);
    }

    [Fact]
    public async Task GivenANotReferencedPathButNoDefinitionManager_WhenResolved_ThenThrowsRatherThanSilentlyWidening()
    {
        // Arrange -- Patient?_not-referenced=Observation:subject with no ISearchParameterDefinitionManager.
        // The path cannot be resolved, so omitting it would widen the anti-join to path-agnostic and return
        // more resources than asked. That is a missing-dependency programmer error, not an unresolvable
        // path, so Resolve throws -- the same contract as compartment membership.
        var expression = new NotReferencedExpression("Observation", "subject");
        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 96;

        // Act & Assert -- no searchParameterDefinitionManager supplied
        await Should.ThrowAsync<InvalidOperationException>(() => ResolveHarness.RunAsync(
            expression, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None));
    }

    [Fact]
    public async Task GivenAFullWildcardNotReferencedAndNoDefinitionManager_WhenResolved_ThenDoesNotThrow()
    {
        // Arrange -- Patient?_not-referenced=*:* needs no path resolution (no Type:path pair is collected),
        // so a missing definition manager is harmless here and must not trip the guard.
        var expression = new NotReferencedExpression(sourceResourceType: null, referencePath: null);
        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = (await ResolveHarness.RunAsync(
            expression, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None)).Symbols;

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
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

        public Dictionary<string, int> SystemIds { get; } = [];

        public Dictionary<string, int> QuantityCodeIds { get; } = [];

        /// <summary>Tracks how many times <see cref="GetSystemIdAsync"/> was called per system, for deduplication assertions.</summary>
        public Dictionary<string, int> SystemIdCallCounts { get; } = [];

        /// <summary>Tracks how many times <see cref="GetQuantityCodeIdAsync"/> was called per code, for deduplication assertions.</summary>
        public Dictionary<string, int> QuantityCodeIdCallCounts { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
        {
            var url = parameter.Url?.ToString();
            return Task.FromResult(url != null && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);
        }

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);

        public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
        {
            SystemIdCallCounts[system] = SystemIdCallCounts.GetValueOrDefault(system) + 1;
            return Task.FromResult(SystemIds.TryGetValue(system, out var id) ? (int?)id : null);
        }

        public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
        {
            QuantityCodeIdCallCounts[code] = QuantityCodeIdCallCounts.GetValueOrDefault(code) + 1;
            return Task.FromResult(QuantityCodeIds.TryGetValue(code, out var id) ? (int?)id : null);
        }
    }
}
