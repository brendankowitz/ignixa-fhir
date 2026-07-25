using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
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
        var plan = Lower.Run(predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null, top: 10).Plan;

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
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
    public void GivenAWrappedNotModifiedTokenPredicate_WhenLowered_ThenLowersAsANegationWithoutReachingTheTokenModifierGuard()
    {
        // Arrange -- TokenLoweringRule now throws for any modifier it does not implement, which is only
        // safe because :not is rewritten into a negation by LowerSearchParameter before leaf dispatch.
        // This pins that premise: the shape the real binder produces must still lower.
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));
        var expression = new SearchParameterExpression(parameter, Expression.Not(predicate));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 44 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(expression, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Ctes.ShouldContain(c => c is CteDefinition.ParamSource);
        plan.Ctes.ShouldContain(c => c is CteDefinition.Except);
    }

    [Fact]
    public void GivenAMissingTokenParameter_WhenLowered_ThenLowersThroughItsOwnNodeKindWithoutReachingTheTokenModifierGuard()
    {
        // Arrange -- :missing lowers through MissingSearchParameterExpression, never carrying the
        // modifier down to TokenLoweringRule, so the new guard must not fire on it.
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var expression = new MissingSearchParameterExpression(parameter, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 44 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(expression, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Ctes.ShouldContain(c => c is CteDefinition.Except);
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
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate, nonIterate], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [encounterIterate, conditionIterate], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [include], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Includes!.Count.ShouldBe(1);
        plan.Includes[0].ReferenceSearchParamId.ShouldBeNull();
        plan.Includes[0].OutputTypeIds.ShouldBeNull();
        plan.Includes[0].SeedTypeIds.ShouldBe([(short)103]);
    }

    [Fact]
    public void GivenAReversedIncludeExpressionPassedIntoTheForwardIncludesList_WhenLowered_ThenDirectionIsStillReverse()
    {
        // Arrange -- a _revinclude-shaped IncludeExpression (Reversed=true), but deliberately passed into
        // BuildIncludeStages' forward `includes` parameter instead of `revIncludes`. Before the fix,
        // Direction came from the caller's list choice (Forward here) while Requires/Produces reflected
        // the expression's true reversed semantics -- an internally inconsistent IncludeStage. Direction
        // must be derived from expression.Reversed, not from which list it arrived through.
        var conditionSubjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Condition-subject"), targetResourceTypes: ["Patient"]);
        var misplacedRevInclude = new IncludeExpression(["Condition"], conditionSubjectParam, "Condition", "Patient", null, wildCard: false, reversed: true, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [conditionSubjectParam.Url.ToString()] = 21 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Condition"] = 110 });

        // Act -- note: misplacedRevInclude is passed as `includes` (forward), not `revIncludes`.
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [misplacedRevInclude], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Includes!.Count.ShouldBe(1);
        plan.Includes[0].Direction.ShouldBe(IncludeDirection.Reverse);
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
        var plan = Lower.Run(tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
            sort: [new SortExpression(nameParam, Ignixa.Search.Expressions.SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null).Plan;

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
            sort: [new SortExpression(lastUpdatedParam, Ignixa.Search.Expressions.SortOrder.Descending)], sortPhase: SortPhase.Valued, page: null).Plan;

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

    [Fact]
    public void GivenAWildcardCompartmentSearchWithBothSortAndIncludes_WhenLowered_ThenTheSortViolationIsReportedFirst()
    {
        // Arrange -- GET /Patient/123/*?_sort=name&_include=Observation:encounter
        // The sort guard (line ~63-69) should fire before the includes guard (line ~72-80).
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var encounterParam = new SearchParameterInfo(
            "encounter", "encounter", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-encounter"), targetResourceTypes: ["Encounter"]);
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var include = new IncludeExpression(["Observation"], encounterParam, "Observation", "Encounter", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77, [nameParam.Url.ToString()] = 202, [encounterParam.Url.ToString()] = 88 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104, ["Encounter"] = 105 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert
        var ex = Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                compartment, symbols, targetResourceType: null, includes: [include], revIncludes: [], includeLimit: 1000,
                sort: [new SortExpression(nameParam, Ignixa.Search.Expressions.SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null));
        ex.Message.ShouldContain("wildcard compartment search");
        ex.Message.ShouldNotContain("SeedFromMatch");
    }

    [Fact]
    public void GivenALastUpdatedPrimarySortKeyInTheMissingPrimaryPhase_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- _lastUpdated is a resource-column key derived from ResourceSurrogateId, so it can
        // never be "missing." A caller driving the two-phase transition (SortPhase is a caller input,
        // not something Lower computes) must never be able to construct a SortSpec([LastUpdated key],
        // MissingPrimary) -- EmitMissingPrimaryFilter would otherwise interpolate a null SearchParamId
        // into SQL text.
        var lastUpdated = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(lastUpdated, Ignixa.Search.Expressions.SortOrder.Ascending)],
                sortPhase: SortPhase.MissingPrimary, page: null))
            .Message.ShouldContain("never");
    }

    [Fact]
    public void GivenCountOnlyTrue_WhenLowered_ThenQueryPlanCountOnlyIsTrue()
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
            sort: [], sortPhase: SortPhase.Valued, page: null, countOnly: true).Plan;

        // Assert
        plan.CountOnly.ShouldBeTrue();
    }

    [Fact]
    public void GivenCountOnlyOmitted_WhenLowered_ThenQueryPlanCountOnlyDefaultsFalse()
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
            sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.CountOnly.ShouldBeFalse();
    }

    [Fact]
    public void GivenAMissingFalseOnAStringParameter_WhenLowered_ThenThePlanIsAParamSourceWithNoPredicate()
    {
        // Arrange -- Patient?name:missing=false ("name is present").
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var missing = new MissingSearchParameterExpression(nameParam, isMissing: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Explain().ShouldBe("root = StringSearchParam[103,202]");
    }

    [Fact]
    public void GivenAMissingTrueOnAStringParameter_WhenLowered_ThenThePlanIsAnExceptOfResourceSourceAndParamSource()
    {
        // Arrange -- Patient?name:missing=true ("name is absent") -- reuses :not's Except/ResourceSource shape.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var missing = new MissingSearchParameterExpression(nameParam, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Except>();
        var except = (CteDefinition.Except)plan.Ctes[plan.Match.Index];
        plan.Ctes[except.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.Ctes[except.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>();
        ((CteDefinition.ParamSource)plan.Ctes[except.Right.Index]).Predicate.ShouldBeNull();
    }

    [Theory]
    [InlineData("_id")]
    [InlineData("_type")]
    [InlineData("_lastUpdated")]
    public void GivenMissingOnAResourceColumnParameter_WhenLowered_ThenThrowsNotSupportedException(string code)
    {
        // Arrange -- _id/_type/_lastUpdated:missing=true is nonsensical (these are never absent) and
        // must throw loudly, not silently compile a query against the wrong table.
        var param = new SearchParameterInfo(code, code, SearchParamType.String, new Uri($"http://hl7.org/fhir/SearchParameter/Resource-{code}"));
        var missing = new MissingSearchParameterExpression(param, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [], sortPhase: SortPhase.Valued, page: null));
    }

    [Fact]
    public void GivenMissingOnAnUnsupportedParameterType_WhenLowered_ThenThrowsNotSupportedExceptionCitingTheType()
    {
        // Arrange -- Special is not a leaf type this compiler handles at all.
        var param = new SearchParameterInfo("composition", "composition", SearchParamType.Special, new Uri("http://hl7.org/fhir/SearchParameter/special-composition"));
        var missing = new MissingSearchParameterExpression(param, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("Special");
    }

    [Fact]
    public void GivenMissingFalseOnATokenQuantityCompositeParameter_WhenLowered_ThenThePlanIsAParamSourceAgainstTheCompositeTable()
    {
        // Arrange -- Observation?component-code-value-quantity:missing=false.
        var tokenComponent = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"));
        var quantityComponent = new SearchParameterInfo("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/clinical-value-quantity"));
        var composite = new SearchParameterInfo(
            "component-code-value-quantity", "component-code-value-quantity", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"),
            components: new[]
            {
                new SearchParameterComponentInfo(tokenComponent.Url, "code") { ResolvedSearchParameter = tokenComponent },
                new SearchParameterComponentInfo(quantityComponent.Url, "value.as(Quantity)") { ResolvedSearchParameter = quantityComponent },
            });
        var missing = new MissingSearchParameterExpression(composite, isMissing: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [composite.Url.ToString()] = 909 },
            new Dictionary<string, short> { ["Observation"] = 104 });

        // Act
        var plan = Lower.Run(
            missing, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Explain().ShouldBe("root = TokenQuantityCompositeSearchParam[104,909]");
    }

    [Fact]
    public void GivenMissingOnACompositeWithNoMatchingTable_WhenLowered_ThenThrowsNotSupportedExceptionCitingTheComponentTypes()
    {
        // Arrange -- a synthetic, unsupported composite shape (Number+Number, no such table exists).
        var numberComponent1 = new SearchParameterInfo("a", "a", SearchParamType.Number, new Uri("http://example.org/a"));
        var numberComponent2 = new SearchParameterInfo("b", "b", SearchParamType.Number, new Uri("http://example.org/b"));
        var composite = new SearchParameterInfo(
            "unsupported-composite", "unsupported-composite", SearchParamType.Composite,
            new Uri("http://example.org/unsupported-composite"),
            components: new[]
            {
                new SearchParameterComponentInfo(numberComponent1.Url, "a") { ResolvedSearchParameter = numberComponent1 },
                new SearchParameterComponentInfo(numberComponent2.Url, "b") { ResolvedSearchParameter = numberComponent2 },
            });
        var missing = new MissingSearchParameterExpression(composite, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("Number");
    }

    [Fact]
    public void GivenMissingOnACompositeWithAnUnresolvedComponent_WhenLowered_ThenThrowsNotSupportedExceptionNotNullReferenceException()
    {
        // Arrange -- a composite with one component where ResolvedSearchParameter is null (unresolved at load time).
        var tokenComponent = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"));
        var unresolvedComponentUrl = new Uri("http://example.org/unresolved");
        var composite = new SearchParameterInfo(
            "composite-with-unresolved", "composite-with-unresolved", SearchParamType.Composite,
            new Uri("http://example.org/composite-with-unresolved"),
            components: new[]
            {
                new SearchParameterComponentInfo(tokenComponent.Url, "code") { ResolvedSearchParameter = tokenComponent },
                new SearchParameterComponentInfo(unresolvedComponentUrl, "unresolved") { ResolvedSearchParameter = null },
            });
        var missing = new MissingSearchParameterExpression(composite, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert -- should throw NotSupportedException, not NullReferenceException
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("unresolved");
    }

    [Fact]
    public void GivenAnApproximationReferenceTime_WhenStructuralContextConstructed_ThenLeafContextCarriesTheExactInstant()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var context = new StructuralContext(symbols, fixedTime);

        // Assert
        context.LeafContext.ApproximationReferenceTime.ShouldBe(fixedTime);
    }

    [Fact]
    public void GivenAnExplicitApproximationReferenceTime_WhenLowered_ThenThePlanProducesSuccessfully()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, approximationReferenceTime: fixedTime).Plan;

        // Assert
        plan.Ctes.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenANegatedPredicateAndedWithAPositiveOne_WhenLowered_ThenSubtractsFromThePositiveRatherThanScanningEveryResource()
    {
        // Arrange -- Patient?name=Smith&active:not=true. Anchoring the negation on a ResourceSource makes
        // the plan read every resource of the type just to subtract from it; the positive sibling is
        // already a strictly smaller set and (A and not B) is (A except B), so it is the better anchor.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var name = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var notActive = new SearchParameterExpression(
            activeParam,
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new TokenSearchValue(system: null, code: "true", text: null)));
        var tree = new MultiaryExpression(MultiaryOperator.And, [name, notActive]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202, [activeParam.Url.ToString()] = 44 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Ctes.ShouldNotContain(cte => cte is CteDefinition.ResourceSource);
        var except = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Except>();
        plan.Ctes[except.Left.Index].ShouldBeOfType<CteDefinition.ParamSource>().SearchParamId.ShouldBe((short)202);
        plan.Ctes[except.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>().SearchParamId.ShouldBe((short)44);
    }

    [Fact]
    public void GivenANegatedPredicateWithNoPositiveSibling_WhenLowered_ThenStillAnchorsOnTheResourceSource()
    {
        // Arrange -- Patient?active:not=true. There is no smaller set to subtract from, so the full
        // resource set remains the only correct anchor.
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var tree = new SearchParameterExpression(
            activeParam,
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new TokenSearchValue(system: null, code: "true", text: null)));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [activeParam.Url.ToString()] = 44 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Ctes.ShouldContain(cte => cte is CteDefinition.ResourceSource);
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Except>();
    }

    [Fact]
    public void GivenAMultiValuedIdParameter_WhenLowered_ThenLiftsTheWholeOrIntoTheOuterWhere()
    {
        // Arrange -- Patient?_id=a,b,c. A comma list binds to one SearchParameterExpression wrapping an
        // Or of predicates, not a bare predicate, so the single-predicate shape the extraction pass
        // recognised let it fall through to CTE lowering -- where the leaf dispatcher throws on purpose
        // rather than route a resource column into an unrelated search-param table.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var alternatives = Expression.Or(
        [
            new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "a", text: null)),
            new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "b", text: null)),
            new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "c", text: null)),
        ]);
        var tree = new SearchParameterExpression(idParam, alternatives);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- no search-param CTE is needed for a query that only filters resource columns
        plan.OuterPredicate.ShouldNotBeNull();
        plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ResourceSource>();
    }

    [Fact]
    public void GivenANegatedMultiValuedIdParameter_WhenLowered_ThenLiftsANegatedOrIntoTheOuterWhere()
    {
        // Arrange -- Observation?_id:not=a,b. The binder wraps a negated comma list as
        // NotExpression(Or([_id=a, _id=b])), each alternative losing its own modifier. It must lift into
        // the outer WHERE as NOT (ResourceId = @p0 OR ResourceId = @p1), not fall through to CTE lowering
        // where the leaf dispatcher rejects resource columns.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var tree = new SearchParameterExpression(
            idParam,
            new NotExpression(Expression.Or(
            [
                new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "a", text: null)),
                new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "b", text: null)),
            ])));
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Observation"] = 96 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var not = plan.OuterPredicate.ShouldBeOfType<Predicate.Not>();
        var or = not.Operand.ShouldBeOfType<Predicate.Or>();
        or.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceId");
        or.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceId");
        plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ResourceSource>();
    }

    [Fact]
    public void GivenASingleValuedNegatedIdParameter_WhenLowered_ThenLiftsABarePredicateUnderNotIntoTheOuterWhere()
    {
        // Arrange -- Patient?_id:not=a. The binder still wraps a single value as NotExpression(Or([_id=a])),
        // so the outer predicate is Not(Equal) directly (a bare predicate under Not), a distinct shape from
        // the multi-value Not(Or(...)) case. Pins that the one-element Or collapses to the equality without
        // a spurious Or wrapper.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var tree = new SearchParameterExpression(
            idParam,
            new NotExpression(Expression.Or(
            [
                new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "a", text: null)),
            ])));
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var not = plan.OuterPredicate.ShouldBeOfType<Predicate.Not>();
        not.Operand.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceId");
        plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ResourceSource>();
    }

    [Fact]
    public void GivenANotReferencedSourceAndPath_WhenLowered_ThenProducesANotReferencedSourceCteWithResolvedIds()
    {
        // Arrange -- Patient?_not-referenced=Observation:subject.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var tree = new NotReferencedExpression("Observation", "subject");
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 969 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 96 },
            notReferencedPaths: new Dictionary<(string, string), SearchParameterInfo> { [("Observation", "subject")] = subjectParam });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var source = plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.NotReferencedSource>();
        source.TargetResourceTypeId.ShouldBe((short)103);
        source.SourceResourceTypeId.ShouldBe((short)96);
        source.ReferenceSearchParamId.ShouldBe((short)969);
    }

    [Fact]
    public void GivenAFullWildcardNotReferenced_WhenLowered_ThenNeitherSourceNorPathIsSet()
    {
        // Arrange -- Patient?_not-referenced=*:*.
        var tree = new NotReferencedExpression(sourceResourceType: null, referencePath: null);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var source = plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.NotReferencedSource>();
        source.TargetResourceTypeId.ShouldBe((short)103);
        source.SourceResourceTypeId.ShouldBeNull();
        source.ReferenceSearchParamId.ShouldBeNull();
    }

    [Fact]
    public void GivenAPathWildcardNotReferenced_WhenLowered_ThenSourceIsSetButPathIsNot()
    {
        // Arrange -- Patient?_not-referenced=Observation:* -- source type narrows the anti-join, but no
        // single reference path does.
        var tree = new NotReferencedExpression("Observation", referencePath: null);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 96 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var source = plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.NotReferencedSource>();
        source.SourceResourceTypeId.ShouldBe((short)96);
        source.ReferenceSearchParamId.ShouldBeNull();
    }

    [Fact]
    public void GivenANotReferencedAndedWithAnIdentifier_WhenLowered_ThenIntersectsTheAntiJoinWithTheParamSource()
    {
        // Arrange -- Patient?_not-referenced=Observation:subject&identifier=... -- the anti-join composes
        // with an ordinary predicate the same way any two leaves do.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var identifierParam = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new NotReferencedExpression("Observation", "subject"),
            new SearchParameterExpression(
                identifierParam,
                new SearchParameterPredicateExpression(identifierParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://ignixa.io/testscript/suite/ms-not-referenced", code: null, text: null))),
        ]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 969, [identifierParam.Url.ToString()] = 1013 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 96 },
            notReferencedPaths: new Dictionary<(string, string), SearchParameterInfo> { [("Observation", "subject")] = subjectParam },
            systemIds: new Dictionary<string, int?> { ["http://ignixa.io/testscript/suite/ms-not-referenced"] = 5 });

        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var intersect = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Intersect>();
        plan.Ctes[intersect.Left.Index].ShouldBeOfType<CteDefinition.NotReferencedSource>();
        plan.Ctes[intersect.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>();
    }

    [Fact]
    public void GivenSeveralResourceTypesAndNoExpression_WhenLowered_ThenTheMatchSetSpansAllOfThem()
    {
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 });

        var plan = Lower.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            resourceTypes: ["Patient", "Observation"]).Plan;

        // Assert against the AST node: the type mapping is what is under test, not emitter formatting.
        var mts = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>();
        mts.ResourceTypeIds.ShouldBe([103, 104]);
    }

    [Fact]
    public void GivenNoResourceTypeAtAll_WhenLowered_ThenTheMatchSetIsEveryType()
    {
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        var plan = Lower.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            resourceTypes: []).Plan;

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldNotContain("ResourceTypeId =");
        sql.ShouldNotContain("ResourceTypeId IN");
    }

    [Fact]
    public void GivenAMultiTypeSearchWithAnUnresolvableTypeName_WhenLowered_ThenTheSentinelIsKeptToAvoidWideningToAllTypes()
    {
        // An unresolvable name yields the sentinel -1 from _leafContext.ResourceTypeId. The sentinel is
        // kept in the IN list rather than dropped: dropping it when every name is unresolvable would
        // collapse the list to empty, which means "all types" — catastrophically wrong. IN (-1) matches
        // nothing, which is the correct answer for a type the catalog does not know.
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        var plan = Lower.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            resourceTypes: ["Patient", "NotAType"]).Plan;

        var sql = SqlBuilder.Run(plan).Sql;

        // The sentinel -1 is present; the query matches no row for the unknown type but does not widen.
        sql.ShouldContain("ResourceTypeId IN (103, -1)");
    }

    [Fact]
    public void GivenAllUnresolvableTypeNames_WhenLowered_ThenTheInListContainsSentinelsNotAllTypes()
    {
        // When every requested type is unknown, the IN list is IN(-1) rather than empty.
        // An empty IN list would be dropped, producing a full-table scan — wrong and dangerous.
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        var plan = Lower.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            resourceTypes: ["NotAType"]).Plan;

        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("ResourceTypeId IN (-1)");
        sql.ShouldNotContain("ResourceTypeId IN ()");
    }

    [Fact]
    public void GivenASingleResourceTypeInTheList_WhenLowered_ThenEmitsInWithOneElement()
    {
        // A single-element list emits IN (x) rather than = x for consistency — IN (x) is equally
        // valid T-SQL and avoids a special-case branch in the emitter.
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        var plan = Lower.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            resourceTypes: ["Patient"]).Plan;

        // Assert the AST mapping first; then confirm the emitter path uses IN rather than = for a
        // one-element list (this is the one emitted-SQL assertion kept to cover the emitter path).
        var mts = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>();
        mts.ResourceTypeIds.ShouldBe([103]);
        SqlBuilder.Run(plan).Sql.ShouldContain("ResourceTypeId IN (103)");
    }

    [Fact]
    public void GivenAnEmptyTypeListPassedToForTypes_WhenConstructed_ThenThrows()
    {
        // ForTypes([]) must throw rather than silently producing an AllTypes scan. This is the
        // API-level protection: a caller that filters a type list down to nothing gets an error
        // rather than a full-table scan.
        Should.Throw<ArgumentException>(() => CteDefinition.MultiTypeResourceSource.ForTypes([]));
    }
}

