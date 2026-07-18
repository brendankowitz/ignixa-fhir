using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class LowerTests
{
    [Fact]
    public void GivenASingleLeafPredicate_WhenLowered_ThenProducesAOneCteQueryPlan()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Ctes.Count.ShouldBe(1);
        plan.Match.ShouldBe(new CteRef(0));
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
    }

    [Fact]
    public void GivenTwoAndedLeafPredicates_WhenLowered_ThenProducesAnIntersectOverBothCtes()
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var namePredicate = new SearchParameterPredicateExpression(
            nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var activePredicate = new SearchParameterPredicateExpression(
            activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));
        var tree = new MultiaryExpression(MultiaryOperator.And, [namePredicate, activePredicate]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202, [activeParam.Url.ToString()] = 44 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null, top: 10);

        // Assert
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[2].ShouldBeOfType<CteDefinition.Intersect>();
        plan.Match.ShouldBe(new CteRef(2));
        plan.Top.ShouldBe(10);
    }

    [Fact]
    public void GivenASingleElementAndTree_WhenLowered_ThenProducesNoIntersectNodeAndMatchesTheLeafDirectly()
    {
        // Arrange -- MultiaryExpression enforces a non-empty Expressions list, but a single-element
        // And is still a legal shape; LowerAnd must not synthesize a spurious Intersect(x, x) node.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var tree = new MultiaryExpression(MultiaryOperator.And, [predicate]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Ctes.Count.ShouldBe(1);
        plan.Match.ShouldBe(new CteRef(0));
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
    }

    [Fact]
    public void GivenABareNotExpressionOutsideASearchParameterExpressionWrapper_WhenLowered_ThenThrowsBecauseTheGenericDispatcherRejectsIt()
    {
        // Arrange -- :not is only wired up inside LowerSearchParameter (reached via the
        // SearchParameterExpression case), which the real binder always uses to carry a
        // NotExpression. A bare, unwrapped NotExpression matches none of LowerNode's switch arms
        // (it isn't a SearchParameterPredicateExpression, SearchParameterExpression, or
        // MultiaryExpression), so it falls to the generic "Lower does not support X yet" throw.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var notExpression = Expression.Not(predicate);
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(notExpression, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("does not support");
    }

    [Fact]
    public void GivenABareNotModifiedPredicateOutsideASearchParameterExpressionWrapper_WhenLowered_ThenThrowsRatherThanSilentlyMatchingPositively()
    {
        // Arrange -- the real binder always wraps a :not-modified predicate in SearchParameterExpression
        // (LowerSearchParameter is where :not is actually handled), so this shape never occurs in
        // practice. This is a defense-in-depth guard: if it ever did occur (a hand-built tree, or a
        // future binder change), the old bug this test guards against was LowerNode's leaf case
        // silently lowering it as a positive match instead of a negation -- a real bug this plan's
        // Task 5 review caught for the SearchParameterExpression-wrapped shape, closed here for the
        // unwrapped shape too.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new StringSearchValue("Smith"));
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null));
    }

    [Fact]
    public void GivenAPredicateWithAnUnsupportedSearchValueType_WhenLowered_ThenThrowsRatherThanSilentlyDroppingIt()
    {
        // Arrange -- CompositeIndexSearchValue has no tier-1 lowering rule (composites are out of
        // scope for this plan); the dispatcher must throw, not fall through to one of the handled rules.
        var parameter = new SearchParameterInfo("component-value-quantity", "component-value-quantity", SearchParamType.Composite, new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-value-quantity"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new CompositeIndexSearchValue([[new QuantitySearchValue(system: null!, code: null!, 5.4m)]]));
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short> { ["Observation"] = 104 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(predicate, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null));
    }

    [Fact]
    public void GivenAnIncludeOnlySearchWithNoOtherExpression_WhenLowered_ThenTheMatchFallsBackToResourceSource()
    {
        // Arrange -- Patient?_include=Patient:organization, no other filter (expression is null).
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [orgParam.Url.ToString()] = 55 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        // Act
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Ctes.Count.ShouldBe(1);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.Includes.ShouldNotBeNull();
        plan.Includes!.Count.ShouldBe(1);
        plan.Includes[0].Direction.ShouldBe(IncludeDirection.Forward);
        plan.Includes[0].ReferenceSearchParamId.ShouldBe((short)55);
        plan.Includes[0].SeedTypeIds.ShouldBe([(short)103]);
        plan.Includes[0].OutputTypeIds.ShouldBe([(short)105]);
        plan.Includes[0].SeedFromMatch.ShouldBeTrue();
        plan.Includes[0].SeedStages.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTwoIterateIncludesThatChainProducesToRequires_WhenLowered_ThenTheSecondStageSeedsFromTheFirst()
    {
        // Arrange -- Patient?_include:iterate=Organization:partOf&_include=Patient:organization
        // (:iterate stage requires Organization, which the non-iterate stage produces).
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var partOfParam = new SearchParameterInfo(
            "partof", "partof", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"), targetResourceTypes: ["Organization"]);
        var nonIterate = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var iterate = new IncludeExpression(["Organization"], partOfParam, "Organization", "Organization", null, wildCard: false, reversed: false, iterate: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [orgParam.Url.ToString()] = 55, [partOfParam.Url.ToString()] = 66 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        // Act -- iterate entry listed FIRST in the includes list, to prove ordering is by the sort, not input order.
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate, nonIterate], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert -- non-iterate stage always sorts first (design §4.1); inc0 is Organization:organization, inc1 is the iterate.
        plan.Includes!.Count.ShouldBe(2);
        plan.Includes[0].ReferenceSearchParamId.ShouldBe((short)55);
        plan.Includes[0].SeedFromMatch.ShouldBeTrue();
        plan.Includes[1].ReferenceSearchParamId.ShouldBe((short)66);
        plan.Includes[1].SeedStages.ShouldBe([0]);
        plan.Includes[1].SeedFromMatch.ShouldBeFalse();
    }

    [Fact]
    public void GivenTwoIndependentIterateIncludesThatBecomeReadySimultaneously_WhenLowered_ThenTheOriginalListOrderIsPreservedAsTheDeterministicTieBreak()
    {
        // Arrange -- Patient?_revinclude:iterate=Condition:subject&_revinclude:iterate=Encounter:subject.
        // Neither stage's Produces overlaps the other's Requires (both just require Patient, satisfied
        // directly by the match) -- both are simultaneously "ready" in Kahn's first round, with no edge
        // between them. Without the deterministic lowest-original-index tie-break (design §4.5), which
        // one sorts first would be an implementation accident, breaking Explain() golden-string stability.
        var conditionSubjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Condition-subject"), targetResourceTypes: ["Patient"]);
        var encounterSubjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Encounter-subject"), targetResourceTypes: ["Patient"]);
        var conditionIterate = new IncludeExpression(["Condition"], conditionSubjectParam, "Condition", "Patient", null, wildCard: false, reversed: true, iterate: true);
        var encounterIterate = new IncludeExpression(["Encounter"], encounterSubjectParam, "Encounter", "Patient", null, wildCard: false, reversed: true, iterate: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [conditionSubjectParam.Url.ToString()] = 21, [encounterSubjectParam.Url.ToString()] = 22 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Condition"] = 110, ["Encounter"] = 111 });

        // Act -- Encounter listed first in the input list.
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [encounterIterate, conditionIterate], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert -- inc0 is the Encounter stage (ref=22), matching its position in the input list.
        plan.Includes!.Count.ShouldBe(2);
        plan.Includes[0].ReferenceSearchParamId.ShouldBe((short)22);
        plan.Includes[1].ReferenceSearchParamId.ShouldBe((short)21);
    }

    [Fact]
    public void GivenTwoMutuallyDependentIterateIncludes_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- two :iterate expressions whose Produces/Requires form a genuine 2-node cycle.
        var aParam = new SearchParameterInfo(
            "a", "a", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/A-a"), targetResourceTypes: ["B"]);
        var bParam = new SearchParameterInfo(
            "b", "b", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/B-b"), targetResourceTypes: ["A"]);
        var includeA = new IncludeExpression(["A"], aParam, "A", "B", null, wildCard: false, reversed: false, iterate: true);
        var includeB = new IncludeExpression(["B"], bParam, "B", "A", null, wildCard: false, reversed: false, iterate: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [aParam.Url.ToString()] = 1, [bParam.Url.ToString()] = 2 },
            new Dictionary<string, short> { ["A"] = 10, ["B"] = 11, ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [includeA, includeB], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("cycle");
    }

    [Fact]
    public void GivenAnIterateIncludeThatNeitherAPredecessorProducesNorTheMatchRequires_WhenLowered_ThenTheStageIsDroppedEntirely()
    {
        // Arrange -- Patient?_include:iterate=Organization:partOf with NO non-iterate Organization-
        // producing include and Patient not being Organization -- Requires=[Organization] intersects
        // neither any predecessor's Produces (there is none) nor the match's own type (Patient).
        var partOfParam = new SearchParameterInfo(
            "partof", "partof", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"), targetResourceTypes: ["Organization"]);
        var iterate = new IncludeExpression(["Organization"], partOfParam, "Organization", "Organization", null, wildCard: false, reversed: false, iterate: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [partOfParam.Url.ToString()] = 66 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        // Act
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert -- the degenerate stage was dropped, not emitted with an empty EXISTS.
        plan.Includes.ShouldBeNull();
    }

    [Fact]
    public void GivenARevincludeWildcardSourceInclude_WhenLowered_ThenOutputTypeIdsIsNullNotAResolvedStarEntry()
    {
        // Arrange -- Patient?_revinclude=*:*
        var include = new IncludeExpression(["*"], null, "*", "Patient", ["Observation"], wildCard: true, reversed: true, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 });

        // Act
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [include], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Includes!.Count.ShouldBe(1);
        plan.Includes[0].ReferenceSearchParamId.ShouldBeNull();
        plan.Includes[0].OutputTypeIds.ShouldBeNull();
        plan.Includes[0].SeedTypeIds.ShouldBe([(short)103]);
    }

    [Fact]
    public void GivenAWildcardCompartmentSearchWithAnOrdinaryTypedPredicate_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- GET /Patient/123/*?name=Smith -- no single resource type to scope "name" against.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var namePredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var tree = new MultiaryExpression(MultiaryOperator.And, [compartment, namePredicate]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77, [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("no single resource type");
    }

    [Fact]
    public void GivenAWildcardCompartmentSearchWithAResourceColumnPredicate_WhenLowered_ThenTheCompartmentUnionIsTheMatchAndTheColumnPredicateBecomesTheOuterPredicate()
    {
        // Arrange -- GET /Patient/123/*?_id=456 -- a wildcard compartment search (no single target
        // resource type) combined with an _id resource-column predicate. ExtractResourceColumnPredicates
        // pulls the _id predicate out of the And, leaving a lone CompartmentSearchExpression as the
        // single surviving child (kept.Count == 1); the `kept[0]` unwrap in Lower.cs must hand that
        // child back directly (not re-wrapped in a spurious single-element And) so the remaining
        // switch's CompartmentSearchExpression arm matches and dispatches to LowerCompartment, rather
        // than falling into the "no single resource type" throw meant for ordinary typed predicates.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var idPredicate = new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "456", text: null));
        var idExpression = new SearchParameterExpression(idParam, idPredicate);
        var tree = new MultiaryExpression(MultiaryOperator.And, [compartment, idExpression]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        plan.OuterPredicate.ShouldNotBeNull();
    }

    [Fact]
    public void GivenAWildcardCompartmentSearchWithIncludes_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- GET /Patient/123/*?_include=Observation:encounter
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var encounterParam = new SearchParameterInfo(
            "encounter", "encounter", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-encounter"), targetResourceTypes: ["Encounter"]);
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var include = new IncludeExpression(["Observation"], encounterParam, "Observation", "Encounter", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77, [encounterParam.Url.ToString()] = 88 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104, ["Encounter"] = 105 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(compartment, symbols, targetResourceType: null, includes: [include], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("SeedFromMatch");
    }

    [Fact]
    public void GivenASingleStringSortKey_WhenLowered_ThenPlanSortHasTheResolvedSearchParamId()
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(nameParam, Ignixa.Search.Expressions.SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Sort.ShouldNotBeNull();
        plan.Sort!.Keys.Count.ShouldBe(1);
        plan.Sort.Keys[0].SearchParamId.ShouldBe((short)202);
        plan.Sort.Keys[0].Kind.ShouldBe(SortKeyKind.String);
        plan.Sort.Keys[0].Direction.ShouldBe(Ignixa.Search.Expressions.SortOrder.Ascending);
        plan.Sort.Phase.ShouldBe(SortPhase.Valued);
    }

    [Fact]
    public void GivenALastUpdatedSortKey_WhenLowered_ThenNoSearchParamIdIsRequested()
    {
        // Arrange -- symbols has no SearchParamId entry at all; must not throw.
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(lastUpdatedParam, Ignixa.Search.Expressions.SortOrder.Descending)], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Sort!.Keys[0].SearchParamId.ShouldBeNull();
        plan.Sort.Keys[0].Kind.ShouldBe(SortKeyKind.LastUpdated);
    }

    [Fact]
    public void GivenFourSortKeys_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange
        var p1 = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var p2 = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"));
        var p3 = new SearchParameterInfo("gender", "gender", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-gender"));
        var lastUpdated = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [p1.Url.ToString()] = 1, [p2.Url.ToString()] = 2, [p3.Url.ToString()] = 3 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [
                    new SortExpression(p1, Ignixa.Search.Expressions.SortOrder.Ascending),
                    new SortExpression(p2, Ignixa.Search.Expressions.SortOrder.Descending),
                    new SortExpression(p3, Ignixa.Search.Expressions.SortOrder.Ascending),
                    new SortExpression(lastUpdated, Ignixa.Search.Expressions.SortOrder.Descending),
                ],
                sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("at most 3 keys");
    }

    [Fact]
    public void GivenATokenSortKey_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- Token/Number/Quantity/Reference/Uri sort is deferred, not silently mishandled.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [statusParam.Url.ToString()] = 1 },
            new Dictionary<string, short> { ["Observation"] = 104 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                expression: null, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(statusParam, Ignixa.Search.Expressions.SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("Token");
    }

    [Fact]
    public void GivenAWildcardCompartmentSearchWithASortKey_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- GET /Patient/123/*?_sort=name -- no single resource type to scope the sort join against.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77, [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                compartment, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(nameParam, Ignixa.Search.Expressions.SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("wildcard compartment search");
    }
}
