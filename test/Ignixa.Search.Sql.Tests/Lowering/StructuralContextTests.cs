using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class StructuralContextTests
{
    [Fact]
    public void GivenAResourceColumnPredicateNestedInsideAnUnflattenedAnd_WhenLowered_ThenTheExceptionNamesTheLikelyCause()
    {
        // Arrange -- mirrors the bug found and fixed in SearchCompartmentHandler (Task 2 of this sub-project):
        // composing And(otherExpression, existingAnd) instead of splicing into existingAnd's own children
        // leaves a resource-column predicate (_id) nested one level inside an And that Lower's top-level
        // ExtractResourceColumnPredicates pass never scans into, so it survives to StructuralContext's
        // leaf/composite dispatch choke point instead of being pulled into the outer WHERE.
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var categoryParam = new SearchParameterInfo("category", "category", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-category"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-active"));

        var idExpression = new SearchParameterExpression(
            idParam,
            new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "obs-1", text: null)));
        var categoryExpression = new SearchParameterPredicateExpression(
            categoryParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "laboratory", text: null));
        var existingAnd = new MultiaryExpression(MultiaryOperator.And, [idExpression, categoryExpression]);

        var activePredicate = new SearchParameterPredicateExpression(
            activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));

        var tree = new MultiaryExpression(MultiaryOperator.And, [activePredicate, existingAnd]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [categoryParam.Url.ToString()] = 55, [activeParam.Url.ToString()] = 44 },
            new Dictionary<string, short> { ["Observation"] = 104 });

        // Act & Assert
        var ex = Should.Throw<NotSupportedException>(() =>
            LowerHarness.Run(tree, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null));
        ex.Message.ShouldContain(
            "This commonly happens when a resource-column predicate arrives nested inside an And/Or that " +
            "wasn't flattened before reaching Lower.Run -- e.g. a caller composing And(otherExpression, " +
            "existingAnd) instead of splicing into existingAnd's own children. Flatten the composed " +
            "expression before calling Lower.");
    }
}
