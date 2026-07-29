using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.TestSupport;
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
        var plan = LowerHarness.Run(predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null, new LowerOptions { Top = 10 }).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(notExpression, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null))
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
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null));
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
        var plan = LowerHarness.Run(expression, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(expression, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(predicate, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null));
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
        var plan = LowerHarness.Run(expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate, nonIterate], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [encounterIterate, conditionIterate], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
            LowerHarness.Run(expression: null, symbols, targetResourceType: "Patient", includes: [includeA, includeB], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null))
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
        var plan = LowerHarness.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [include], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(expression: null, symbols, targetResourceType: "Patient", includes: [misplacedRevInclude], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
            LowerHarness.Run(tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null))
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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
            LowerHarness.Run(compartment, symbols, targetResourceType: null, includes: [include], revIncludes: [], includeLimit: 1000, sort: [], sortPhase: SortPhase.Valued, page: null))
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
        var plan = LowerHarness.Run(
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
        var plan = LowerHarness.Run(
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
            LowerHarness.Run(
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
    public void GivenATokenSortKey_WhenLowered_ThenProducesAnAggregatedKeyBoundToTheTokenTable()
    {
        // Arrange -- Token/Number/Quantity/Reference/Uri sort now lowers to an Aggregated key whose
        // table/column come from the catalog. TokenSearchParam carries no IsMin/IsMax column, so the
        // value is resolved by a MIN/MAX-aggregating join rather than a flagged row.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [statusParam.Url.ToString()] = 1 },
            new Dictionary<string, short> { ["Observation"] = 104 });

        // Act
        var plan = LowerHarness.Run(
            expression: null, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(statusParam, Ignixa.Search.Expressions.SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        var key = plan.Sort.ShouldNotBeNull().Keys.ShouldHaveSingleItem();
        key.Kind.ShouldBe(SortKeyKind.Aggregated);
        key.Table!.TableName.ShouldBe("TokenSearchParam");
        key.Column!.Name.ShouldBe("Code");
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
            LowerHarness.Run(
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
            LowerHarness.Run(
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
            LowerHarness.Run(
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
        var plan = LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, new LowerOptions { CountOnly = true }).Plan;

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
        var plan = LowerHarness.Run(
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
        var plan = LowerHarness.Run(
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
        var plan = LowerHarness.Run(
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
            LowerHarness.Run(
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
            LowerHarness.Run(
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
        var plan = LowerHarness.Run(
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
            LowerHarness.Run(
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
            LowerHarness.Run(
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
        var plan = LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, new LowerOptions { ApproximationReferenceTime = fixedTime }).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

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

        var plan = LowerHarness.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null, new LowerOptions { ResourceTypes = ["Patient", "Observation"] }).Plan;

        // Assert against the AST node: the type mapping is what is under test, not emitter formatting.
        var mts = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>();
        mts.ResourceTypeIds.ShouldBe([103, 104]);
    }

    [Fact]
    public void GivenNoResourceTypeAtAll_WhenLowered_ThenTheMatchSetIsEveryType()
    {
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        var plan = LowerHarness.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null, new LowerOptions { ResourceTypes = [] }).Plan;

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

        var plan = LowerHarness.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null, new LowerOptions { ResourceTypes = ["Patient", "NotAType"] }).Plan;

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

        var plan = LowerHarness.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null, new LowerOptions { ResourceTypes = ["NotAType"] }).Plan;

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

        var plan = LowerHarness.Run(
            expression: null,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null, new LowerOptions { ResourceTypes = ["Patient"] }).Plan;

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

    // ─── System-level (cross-type) lowering tests ───────────────────────────────────────────────────

    [Fact]
    public void GivenMultipleTypesAndALeafPredicate_WhenLoweringSystemLevel_ThenBothTypesNarrowAndTheLeafApplies()
    {
        // Arrange -- GET /?_type=Patient,Observation&name=foo. The leaf has no single resource type to
        // scope against, so its ParamSource must carry no ResourceTypeId at all; the two requested types
        // narrow the base set instead.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("foo"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 });

        // Act
        var plan = LowerHarness.Run(
            predicate,
            symbols,
            targetResourceType: null,
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [],
            SortPhase.Valued,
            page: null,
            new LowerOptions { SystemLevelSearch = true, ResourceTypes = ["Patient", "Observation"] }).Plan;

        // Assert
        var intersect = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Intersect>();
        plan.Ctes[intersect.Left.Index].ShouldBeOfType<CteDefinition.ParamSource>().ResourceTypeId.ShouldBeNull();
        plan.Ctes[intersect.Right.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>()
            .ResourceTypeIds.ShouldBe([103, 104]);
    }

    [Fact]
    public void GivenAMultiValuedTypeParameterAndALeafPredicate_WhenLoweringSystemLevel_ThenTheTypeOrLiftsToTheOuterWhereAndTheLeafStaysUntyped()
    {
        // Arrange -- GET /?_type=Patient,Observation&name=foo as the binder actually produces it: the type
        // filter arrives as an expression (an Or of _type equalities under one SearchParameterExpression),
        // not as a caller-supplied LowerOptions.ResourceTypes list. This is the path Ignixa's own Build
        // output takes, and it must reach the outer WHERE rather than falling through to CTE lowering,
        // where the leaf dispatcher rejects resource columns outright.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var tree = new MultiaryExpression(MultiaryOperator.And, [TypeList("Patient", "Observation"), new SearchParameterPredicateExpression(
            nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("foo"))]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url!.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 });

        // Act
        var plan = LowerHarness.Run(
            tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], SortPhase.Valued, page: null, new LowerOptions { SystemLevelSearch = true }).Plan;

        // Assert -- the type list narrows through the outer WHERE, and the leaf carries no type scope.
        var or = plan.OuterPredicate.ShouldBeOfType<Predicate.Or>();
        or.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceTypeId");
        or.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceTypeId");
        plan.Ctes.ShouldHaveSingleItem().ShouldBeOfType<CteDefinition.ParamSource>().ResourceTypeId.ShouldBeNull();
    }

    [Fact]
    public void GivenAMultiValuedTypeParameterNamingAnUnknownType_WhenLowered_ThenTheUnknownBranchStaysInTheOrAsUnsatisfiable()
    {
        // Arrange -- GET /?_type=Patient,NotAType. The extraction is deliberately not restricted to
        // Predicate.Equal branches: an unresolvable type lowers to Predicate.False, which carries the
        // reason for the trace. An Equal-only extraction would reject the whole Or, drop it back to CTE
        // lowering, and turn a diagnosable known miss into a thrown "resource column reached leaf dispatch".
        // Resolve records a type the resolver could not find as the unmatchable sentinel rather than
        // omitting the key -- an omitted key throws KeyNotFoundException on lookup. Mirror that here.
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103, ["NotAType"] = SymbolTable.UnmatchableResourceTypeId });

        // Act
        var plan = LowerHarness.Run(
            TypeList("Patient", "NotAType"), symbols, targetResourceType: null, includes: [], revIncludes: [],
            includeLimit: 0, sort: [], SortPhase.Valued, page: null, new LowerOptions { SystemLevelSearch = true }).Plan;

        // Assert
        var or = plan.OuterPredicate.ShouldBeOfType<Predicate.Or>();
        or.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceTypeId");
        or.Right.ShouldBeOfType<Predicate.False>().Reason.ShouldNotBeNull();
    }

    /// <summary>The shape a bound <c>_type=a,b</c> takes: an Or of bare _type equalities under one wrapper.</summary>
    private static SearchParameterExpression TypeList(params string[] resourceTypes)
    {
        var typeParam = new SearchParameterInfo("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));
        return new SearchParameterExpression(
            typeParam,
            Expression.Or([.. resourceTypes.Select(t => (Expression)new SearchParameterPredicateExpression(
                typeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: t, text: null)))]));
    }

    /// <summary>The shape a bound single-valued <c>_type=X</c> takes: one bare _type equality under its wrapper.
    /// This is the shape a system-level union leg derives its own scope from (see TryDeriveSingleTypeScope).</summary>
    private static SearchParameterExpression SingleType(string resourceType)
    {
        var typeParam = new SearchParameterInfo("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));
        return new SearchParameterExpression(
            typeParam,
            new SearchParameterPredicateExpression(typeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: resourceType, text: null)));
    }

    /// <summary>A patient reference parameter, reused as both a compartment-membership parameter and the target of a
    /// <c>patient:missing=true</c> negation in the system-level union-leg tests.</summary>
    private static readonly SearchParameterInfo PatientRefParam =
        new("patient", "patient", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-patient"));

    // ─── IncludesOnly Lower.Run guard tests ─────────────────────────────────────────────────────────

    [Fact]
    public void GivenLowerRunWithIncludesOnlyAndNoIncludes_WhenCalled_ThenThrowsNotSupportedException()
    {
        // IncludesOnly with no _include/_revinclude parameters can only ever return empty — a caller
        // error rather than a legitimate empty result.
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        Should.Throw<NotSupportedException>(() =>
            LowerHarness.Run(
                expression: null,
                symbols,
                targetResourceType: "Patient",
                includes: [],
                revIncludes: [],
                includeLimit: 0,
                sort: [],
                sortPhase: SortPhase.Valued,
                page: null,
                new LowerOptions { IncludesOnly = true }));
    }

    [Fact]
    public void GivenLowerRunWithIncludesOnlyAndCountOnly_WhenCalled_ThenThrowsNotSupportedException()
    {
        // IncludesOnly asks for include rows; CountOnly counts match rows — these are contradictory.
        // The guard fires before BuildIncludeStages and before the access-constraint binding loop,
        // so the combination is rejected immediately without building any include stages.
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        Should.Throw<NotSupportedException>(() =>
            LowerHarness.Run(
                expression: null,
                symbols,
                targetResourceType: "Patient",
                includes: [],
                revIncludes: [],
                includeLimit: 0,
                sort: [],
                sortPhase: SortPhase.Valued,
                page: null,
                new LowerOptions { IncludesOnly = true, CountOnly = true }));
    }

    [Fact]
    public void GivenLowerRunWithIncludesOnlyAndSort_WhenCalled_ThenCarriesTheSortPhaseAsAFilterRatherThanRefusing()
    {
        // _sort has two roles here, and an includes-only page keeps only one. The ordering role drops -- the
        // page has no match rows to order and pages its include rows by (T1, Sid1) -- but the SortPhase is a
        // *filter* that partitions the match set into rows missing the sort value and rows that have it. The
        // page bounds its match set by a surrogate window and seeds its includes from it, so the phase decides
        // which windowed rows are matches and therefore which resources are included. Lower must carry the sort
        // through (its phase reaches the match-page CTE independently of ORDER BY), not refuse it.
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
        var dateParam = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"));
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [orgParam.Url.ToString()] = 55, [dateParam.Url.ToString()] = 203 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        var plan = LowerHarness.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [include],
            revIncludes: [],
            includeLimit: 1000,
            sort: [new SortExpression(dateParam, Ignixa.Search.Expressions.SortOrder.Ascending)],
            sortPhase: SortPhase.MissingPrimary,
            page: null,
            new LowerOptions { IncludesOnly = true }).Plan;

        // The sort survives lowering with its phase intact -- that is what makes the phase predicate
        // load-bearing on the includes-only match set downstream.
        plan.IncludesOnly.ShouldBeTrue();
        plan.Sort.ShouldNotBeNull();
        plan.Sort!.Phase.ShouldBe(SortPhase.MissingPrimary);
        plan.Sort.Keys.ShouldHaveSingleItem().Kind.ShouldBe(SortKeyKind.Date);
    }

    [Fact]
    public void GivenLowerRunWithIncludesOnlyAndAPage_WhenCalled_ThenThrowsNotSupportedException()
    {
        // A keyset Page seeks the match rows by the sort-key boundary -- a second paging mechanism the
        // includes-only page does not use, since its match window is a surrogate range and its include rows
        // page from a cursor. Letting it through would let the sort key decide which match rows (and therefore
        // which included resources) exist, so it is refused. This is the genuinely unsound combination, as
        // distinct from a sort with no Page, which is allowed for its filtering role.
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [orgParam.Url.ToString()] = 55 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });
        var page = new PageSpec([], BoundaryResourceTypeId: null, BoundarySurrogateId: new SqlParameterRef(4200L));

        Should.Throw<NotSupportedException>(() =>
            LowerHarness.Run(
                expression: null,
                symbols,
                targetResourceType: "Patient",
                includes: [include],
                revIncludes: [],
                includeLimit: 1000,
                sort: [],
                sortPhase: SortPhase.Valued,
                page: page,
                new LowerOptions { IncludesOnly = true }));
    }

    [Fact]
    public void GivenLowerRunWithAnIncludeBoundaryButNotIncludesOnly_WhenCalled_ThenThrowsNotSupportedException()
    {
        // The resume boundary pages the union of include stages as one ordered stream, which exists only on
        // an includes-only page. Without IncludesOnly the emitter keeps the match arm and never applies the
        // resume predicate, so a caller expecting a second page would silently get a full first page back.
        // Refuse it here, mirrored by SqlBuilder for direct QueryPlan callers.
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [orgParam.Url.ToString()] = 55 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        Should.Throw<NotSupportedException>(() =>
            LowerHarness.Run(
                expression: null,
                symbols,
                targetResourceType: "Patient",
                includes: [include],
                revIncludes: [],
                includeLimit: 1000,
                sort: [],
                sortPhase: SortPhase.Valued,
                page: null,
                new LowerOptions { IncludeBoundary = new IncludeBoundary(105, 4200) }));
    }

    [Fact]
    public void GivenLowerRunWithATypedPageAndACustomSort_WhenCalled_ThenThrowsNotSupportedException()
    {
        // A custom (search-parameter) sort emits ORDER BY (sort keys…, Sid1) -- type-free -- but a type on
        // the boundary makes the seek type-major. Within a run of tied sort values a row of a lower type id
        // but higher surrogate id then sorts after the boundary yet is excluded by the seek, and is dropped
        // at the page seam with no error. Refuse it here, mirrored by SqlBuilder for direct QueryPlan callers.
        var nameParam = new SearchParameterInfo(
            "name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        var typedPage = new PageSpec(
            [new SqlParameterRef("Adams")],
            new SqlParameterRef((short)103),
            BoundarySurrogateId: new SqlParameterRef(5000L));

        var ex = Should.Throw<NotSupportedException>(() =>
            LowerHarness.Run(
                expression: null,
                symbols,
                targetResourceType: "Patient",
                includes: [],
                revIncludes: [],
                includeLimit: 0,
                sort: [new SortExpression(nameParam, Ignixa.Search.Expressions.SortOrder.Ascending)],
                sortPhase: SortPhase.Valued,
                page: typedPage));

        ex.Message.ShouldContain("typed keyset Page");
        ex.Message.ShouldContain("page seam");
    }

    [Fact]
    public void GivenLowerRunWithATypelessPageAndANonCustomSort_WhenCalled_ThenThrowsNotSupportedException()
    {
        // The mirror of the guard above, and unsound for the mirrored reason. A typeless boundary breaks its
        // final tie on Sid1 alone, which agrees with the ORDER BY only when the sort is custom. A sortless
        // search orders by (T1, Sid1), so a typeless boundary here would disagree with that type-major
        // ORDER BY and page unsoundly. Refuse it here, mirrored by SqlBuilder for direct QueryPlan callers.
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });
        var typelessPage = new PageSpec(
            [],
            BoundaryResourceTypeId: null,
            BoundarySurrogateId: new SqlParameterRef(7000L));

        var ex = Should.Throw<NotSupportedException>(() =>
            LowerHarness.Run(
                expression: null,
                symbols,
                targetResourceType: "Patient",
                includes: [],
                revIncludes: [],
                includeLimit: 0,
                sort: [],
                sortPhase: SortPhase.Valued,
                page: typelessPage));

        ex.Message.ShouldContain("typeless");
        ex.Message.ShouldContain("custom");
    }

    [Fact]
    public void GivenLowerRunWithATypelessPageAndACustomSort_WhenCalled_ThenTheSortAndPageBothSurviveLowering()
    {
        // The permitted half of the same pairing: a typeless boundary matches the type-free ORDER BY a
        // custom sort emits, so lowering carries both through untouched.
        var nameParam = new SearchParameterInfo(
            "name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        var typelessPage = new PageSpec(
            [new SqlParameterRef("Adams")],
            BoundaryResourceTypeId: null,
            BoundarySurrogateId: new SqlParameterRef(5000L));

        var plan = LowerHarness.Run(
            expression: null,
            symbols,
            targetResourceType: "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 0,
            sort: [new SortExpression(nameParam, Ignixa.Search.Expressions.SortOrder.Ascending)],
            sortPhase: SortPhase.Valued,
            page: typelessPage).Plan;

        plan.Page.ShouldNotBeNull();
        plan.Page!.BoundaryResourceTypeId.ShouldBeNull();
        plan.Sort.ShouldNotBeNull();
        plan.Sort!.Keys.ShouldHaveSingleItem().Kind.ShouldBe(SortKeyKind.String);
    }

    [Fact]
    public void GivenAUnionOfLegs_WhenLowered_ThenEachLegBecomesItsOwnCteJoinedByAUnion()
    {
        // Arrange -- the shape a SMART compartment expands to: several independent row-producing legs, each
        // admitting resources for a different reason, combined so a resource matching any one of them is
        // visible. Written as UnionExpression rather than Or because the legs are set-producing subqueries
        // over different tables, not alternative values of one parameter.
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"));
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var tree = Expression.Union(
            UnionOperator.All,
            [
                new SearchParameterExpression(codeParam, new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "a", text: null))),
                new SearchParameterExpression(statusParam, new SearchParameterPredicateExpression(statusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null))),
            ]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { ["http://hl7.org/fhir/SearchParameter/clinical-code"] = 10, ["http://hl7.org/fhir/SearchParameter/Observation-status"] = 11 },
            new Dictionary<string, short> { ["Observation"] = 96 });

        // Act
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- the match is a union over both legs, not one leg with the other silently dropped. A
        // dropped leg is the dangerous direction here: these legs are what grants access, so losing one
        // hides rows the caller is entitled to, and losing all but one would still look like a working query.
        var union = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        union.Parts.Count.ShouldBe(2);
        union.Parts.Select(part => plan.Ctes[part.Index]).ShouldAllBe(cte => cte is CteDefinition.ParamSource);
    }

    [Fact]
    public void GivenAUnionNestedUnderAnAnd_WhenLowered_ThenTheUnionIsIntersectedWithTheOtherConjunct()
    {
        // Arrange -- the real SMART shape: the access union ANDed with the caller's own filter. The union
        // must narrow the result, never replace it, so a caller searching status=final inside a compartment
        // cannot see a non-final resource just because the compartment admits it.
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"));
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var accessUnion = Expression.Union(
            UnionOperator.All,
            [
                new SearchParameterExpression(codeParam, new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "a", text: null))),
                new SearchParameterExpression(codeParam, new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "b", text: null))),
            ]);
        var tree = Expression.And(
            accessUnion,
            new SearchParameterExpression(statusParam, new SearchParameterPredicateExpression(statusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null))));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { ["http://hl7.org/fhir/SearchParameter/clinical-code"] = 10, ["http://hl7.org/fhir/SearchParameter/Observation-status"] = 11 },
            new Dictionary<string, short> { ["Observation"] = 96 });

        // Act
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Intersect>();
        plan.Ctes.ShouldContain(cte => cte is CteDefinition.Union);
    }

    [Fact]
    public void GivenAUnionLegOfOnlyResourceColumns_WhenLowered_ThenTheLegFoldsIntoItsOwnScopedResourceSource()
    {
        // Arrange -- the SMART compartment's "the compartment resource itself" leg: _id and _type only. At the
        // top level such predicates lift into the outer WHERE, but inside a union that would apply them to
        // every leg and collapse the whole access set to one resource. They must stay scoped to their own leg.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"));
        var tree = Expression.Union(
            UnionOperator.All,
            [
                new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "p1", text: null))),
                new SearchParameterExpression(codeParam, new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "a", text: null))),
            ]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { ["http://hl7.org/fhir/SearchParameter/clinical-code"] = 10 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = LowerHarness.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null).Plan;

        // Assert -- the _id leg is a ResourceSource carrying its own predicate, and nothing leaked outward.
        plan.OuterPredicate.ShouldBeNull();
        var union = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        var idLeg = plan.Ctes[union.Parts[0].Index].ShouldBeOfType<CteDefinition.ResourceSource>();
        idLeg.Predicate.ShouldNotBeNull().ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ResourceId");
        plan.Ctes[union.Parts[1].Index].ShouldBeOfType<CteDefinition.ParamSource>();
    }

    [Fact]
    public void GivenASystemLevelUnionOfPureResourceColumnLegs_WhenLowered_ThenEachLegFoldsIntoAnAllTypesScanCarryingItsPredicate()
    {
        // Arrange -- the SMART compartment shape under a system-level search (GET /?_id=...&_count=100, no
        // _type). The "compartment resource itself" leg is _id+_type; each "universal resource type" leg is a
        // bare _type. Neither has a residue to lower, so each folds into an AllTypes dbo.Resource scan whose
        // WHERE carries the resource-column predicate -- the cross-type analog of the typed ResourceSource fold.
        // This shape used to be refused outright; the refusal was the wrong call, because a purely
        // resource-column leg carries all the scope it needs inside its own predicate.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var tree = Expression.Union(
            UnionOperator.All,
            SingleType("Location"),
            Expression.And(
                new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "c1", text: null))),
                SingleType("Patient")));
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103, ["Location"] = 110 });

        // Act -- must NOT throw.
        var plan = LowerHarness.Run(
            tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], SortPhase.Valued, page: null, new LowerOptions { SystemLevelSearch = true }).Plan;

        // Assert -- nothing lifted to the outer WHERE (that would apply one leg's columns to every leg), and
        // each leg is an AllTypes scan (empty ResourceTypeIds) carrying its own predicate.
        plan.OuterPredicate.ShouldBeNull();
        var union = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        union.Parts.Count.ShouldBe(2);
        foreach (var part in union.Parts)
        {
            var scan = plan.Ctes[part.Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>();
            scan.ResourceTypeIds.ShouldBeEmpty();
            scan.Predicate.ShouldNotBeNull();
        }

        // And the emitted SQL is a full dbo.Resource scan bounded by a WHERE, never an unbounded one.
        var sql = SqlBuilder.Run(plan).Sql;
        sql.ShouldContain("FROM dbo.Resource");
        sql.ShouldContain("ResourceId =");
        sql.ShouldContain("ResourceTypeId =");
    }

    [Fact]
    public void GivenASystemLevelUnionLegPairingATypeWithAMissingNegation_WhenLowered_ThenItDerivesTheTypeAndLowersRatherThanThrowing()
    {
        // Arrange -- the SMART "orphan devices" leg: And(_type=Device, patient:missing=true). Under a
        // system-level search the leg has no ambient type, and a :missing negation cannot anchor its Except on
        // "every resource in the database". The leg's own single _type=Device supplies the anchor -- the very
        // pairing that confines the leg on a typed search -- so the leg must lower, not throw.
        var deviceLeg = Expression.And(
            SingleType("Device"),
            new MissingSearchParameterExpression(PatientRefParam, isMissing: true));
        var tree = Expression.Union(UnionOperator.All, SingleType("Location"), deviceLeg);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [PatientRefParam.Url!.ToString()] = 55 },
            new Dictionary<string, short> { ["Device"] = 120, ["Location"] = 110 });

        // Act -- must NOT throw.
        var plan = LowerHarness.Run(
            tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], SortPhase.Valued, page: null, new LowerOptions { SystemLevelSearch = true }).Plan;

        // Assert -- the device leg lowered to (a Device ResourceSource carrying its _type predicate) INTERSECT
        // (the negation, itself an Except anchored on Device). Because a concrete type was recovered, the leg
        // is indistinguishable from a natively typed one and the negation anchors on Device rather than
        // tripping the null-scope guard.
        var union = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        union.Parts.Count.ShouldBe(2);
        var intersect = plan.Ctes[union.Parts[1].Index].ShouldBeOfType<CteDefinition.Intersect>();
        var scoped = plan.Ctes[intersect.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>();
        scoped.ResourceTypeId.ShouldBe((short)120);
        scoped.Predicate.ShouldNotBeNull();
        var negation = plan.Ctes[intersect.Right.Index].ShouldBeOfType<CteDefinition.Except>();
        plan.Ctes[negation.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>().ResourceTypeId.ShouldBe((short)120);
    }

    [Fact]
    public void GivenASystemLevelUnionLegWithAMultiValuedTypeAndANegation_WhenLowered_ThenNoScopeIsDerivedAndTheNegationIsRefused()
    {
        // Arrange -- a leg pairing a MULTI-valued _type=Device,Location (an Or, not a single equality) with a
        // :missing negation. Deriving a single-type scope from one arm of that Or would silently narrow the
        // leg to Device or Location; instead no scope is derived, the negation lowers under a null type, and
        // its Except anchor -- which has no single type to subtract from -- is refused. Refusal is the correct
        // answer here: the alternative is returning a wrong, silently narrowed row set.
        var leg = Expression.And(
            TypeList("Device", "Location"),
            new MissingSearchParameterExpression(PatientRefParam, isMissing: true));
        var tree = Expression.Union(UnionOperator.All, SingleType("Patient"), leg);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [PatientRefParam.Url!.ToString()] = 55 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Device"] = 120, ["Location"] = 110 });

        // Act & Assert -- the negation-anchor guard fires, naming the real problem (a system-level negation),
        // not a fabricated "the union leg needs a type".
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], SortPhase.Valued, page: null, new LowerOptions { SystemLevelSearch = true }))
            .Message.ShouldContain("system-level");
    }

    [Fact]
    public void GivenASystemLevelUnionWithACompartmentLeg_WhenLowered_ThenTheCompartmentLegLowersUnderANullScope()
    {
        // Arrange -- the SMART "compartment traversal" leg: a bare CompartmentSearchExpression with no _type to
        // derive from. It must lower under a null scope (LowerCompartment needs no resource type) while a
        // sibling universal-type leg folds into an AllTypes scan, proving the null-scope path and the
        // pure-column path coexist inside one union.
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var tree = Expression.Union(UnionOperator.All, compartment, SingleType("Location"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [PatientRefParam.Url!.ToString()] = 55 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Location"] = 110, ["Observation"] = 104 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(PatientRefParam, ["Observation"])],
            });

        // Act -- must NOT throw.
        var plan = LowerHarness.Run(
            tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], SortPhase.Valued, page: null, new LowerOptions { SystemLevelSearch = true }).Plan;

        // Assert -- a CompartmentSource is present (the traversal leg lowered), and the Location leg is an
        // AllTypes scan.
        var union = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        union.Parts.Count.ShouldBe(2);
        plan.Ctes.ShouldContain(cte => cte is CteDefinition.CompartmentSource);
        plan.Ctes[union.Parts[1].Index].ShouldBeOfType<CteDefinition.MultiTypeResourceSource>()
            .ResourceTypeIds.ShouldBeEmpty();
    }
}
