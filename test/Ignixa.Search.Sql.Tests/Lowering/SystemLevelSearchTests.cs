using Ignixa.Search.Expressions;
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

public class SystemLevelSearchTests
{
    [Fact]
    public void GivenAnOrdinaryPredicateWithNoResourceType_WhenLowered_ThenParamSourceHasANullResourceTypeId()
    {
        // Arrange -- GET /?status=final (a Token predicate, no resource type constraint at all).
        var parameter = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null));
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url!.ToString()] = 202 }, new Dictionary<string, short>());

        // Act
        var lowered = Lower.Run(
            predicate, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, systemLevelSearch: true);

        // Assert
        lowered.Plan.Ctes.Count.ShouldBe(1);
        var cte = lowered.Plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
        cte.ResourceTypeId.ShouldBeNull();
        cte.SearchParamId.ShouldBe((short)202);
    }

    [Fact]
    public void GivenABareRequestWithNoPredicatesAtAll_WhenLowered_ThenResourceSourceHasANullResourceTypeId()
    {
        // Arrange -- GET /?_lastUpdated=gt2020-01-01 (a resource-column-only query). The _lastUpdated
        // predicate is a resource-column predicate, so it must be wrapped in a SearchParameterExpression
        // for ExtractResourceColumnPredicates to pull it into the outer WHERE, leaving no CTE-lowerable
        // remainder -- which drops through to the bare ResourceSource base case.
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var predicate = new SearchParameterPredicateExpression(lastUpdatedParam, SearchComparator.Gt, modifier: null, new DateTimeSearchValue(DateTime.Parse("2020-01-01")));
        var expression = new SearchParameterExpression(lastUpdatedParam, predicate);
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        // Act
        var lowered = Lower.Run(
            expression, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, systemLevelSearch: true);

        // Assert
        lowered.Plan.Ctes.OfType<CteDefinition.ResourceSource>().ShouldContain(rs => rs.ResourceTypeId == null);
        lowered.Plan.OuterPredicate.ShouldNotBeNull();
    }

    [Fact]
    public void GivenAnOrdinaryPredicateSortedByAStringKeyWithNoResourceType_WhenLowered_ThenSortComposesNormally()
    {
        // Arrange -- GET /?status=final&_sort=name -- Section 4 requires sort to compose with system-level
        // search. Without the sort guard being gated on the discriminator this would throw unconditionally.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(statusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [statusParam.Url!.ToString()] = 202, [nameParam.Url!.ToString()] = 77 },
            new Dictionary<string, short>());

        // Act -- must NOT throw.
        var lowered = Lower.Run(
            predicate, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(nameParam, Ignixa.Search.Expressions.SortOrder.Ascending)],
            sortPhase: SortPhase.Valued, page: null, systemLevelSearch: true);

        // Assert -- a type-less ParamSource plus a working sort join that emits without error.
        var cte = lowered.Plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
        cte.ResourceTypeId.ShouldBeNull();
        lowered.Plan.Sort.ShouldNotBeNull();
        lowered.Plan.Sort!.Keys.Count.ShouldBe(1);
        lowered.Plan.Sort.Keys[0].SearchParamId.ShouldBe((short)77);

        var emitted = SqlBuilder.Run(lowered.Plan);
        emitted.Sql.ShouldContain("ORDER BY");
        emitted.Sql.ShouldContain("StringSearchParam");
    }

    [Fact]
    public void GivenAChainedExpressionWithNoResourceType_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- ?organization.name=Acme with no target type; chain still requires a known target type.
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"));
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, new SearchParameterExpression(nameParam, innerPredicate));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [orgParam.Url!.ToString()] = 55, [nameParam.Url!.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                chain, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
                sort: [], sortPhase: SortPhase.Valued, page: null, systemLevelSearch: true))
            .Message.ShouldContain("Chain is not supported in system-level search");
    }

    [Fact]
    public void GivenAnIncludeWithNoResourceType_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- ?status=final&_include=Observation:encounter under system-level search. _include does
        // not combine with a null target type: BuildIncludeStages needs a concrete match type for SeedFromMatch.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var encounterParam = new SearchParameterInfo(
            "encounter", "encounter", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-encounter"), targetResourceTypes: ["Encounter"]);
        var predicate = new SearchParameterPredicateExpression(statusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null));
        var include = new IncludeExpression(["Observation"], encounterParam, "Observation", "Encounter", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [statusParam.Url!.ToString()] = 202, [encounterParam.Url!.ToString()] = 88 },
            new Dictionary<string, short> { ["Observation"] = 104, ["Encounter"] = 105 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                predicate, symbols, targetResourceType: null, includes: [include], revIncludes: [], includeLimit: 1000,
                sort: [], sortPhase: SortPhase.Valued, page: null, systemLevelSearch: true))
            .Message.ShouldContain("SeedFromMatch");
    }

    [Fact]
    public void GivenAWildcardCompartmentSearchWithASortKey_WhenLowered_ThenStillThrowsNotSupportedException()
    {
        // Arrange -- THE critical regression guard: a wildcard compartment search (systemLevelSearch
        // defaults to false) is a DIFFERENT null-resourceType case and must STILL throw for combinations
        // it never supported. If this breaks, the discriminator isn't discriminating and system-level
        // search's relaxation leaked into wildcard compartment too. Mirrors LowerTests'
        // GivenAWildcardCompartmentSearchWithASortKey exactly.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url!.ToString()] = 77, [nameParam.Url!.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert -- note: NO systemLevelSearch argument (defaults false), so this stays a genuine
        // wildcard compartment search, not a system-level search.
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                compartment, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(nameParam, Ignixa.Search.Expressions.SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("wildcard compartment search");
    }
}
