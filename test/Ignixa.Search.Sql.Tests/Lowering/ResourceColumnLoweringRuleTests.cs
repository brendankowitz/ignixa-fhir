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

public class ResourceColumnLoweringRuleTests
{
    private static LeafContext ContextResolving(string resourceType, short resourceTypeId)
        => new(new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { [resourceType] = resourceTypeId }));

    private static SearchParameterInfo IdParameter()
        => new("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));

    private static SearchParameterInfo TypeParameter()
        => new("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));

    [Fact]
    public void GivenAnOrdinaryTokenParameter_WhenTried_ThenReturnsNull()
    {
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));

        ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)).ShouldBeNull();
    }

    [Fact]
    public void GivenAnIdParameter_WhenTried_ThenComparesResourceId()
    {
        var predicate = new SearchParameterPredicateExpression(IdParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null));

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        var equal = result.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("ResourceId");
        equal.Value.Value.ShouldBe("123");
    }

    [Fact]
    public void GivenASystemQualifiedIdParameter_WhenTried_ThenThrows()
    {
        var predicate = new SearchParameterPredicateExpression(IdParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://example.org", code: "123", text: null));

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    [Fact]
    public void GivenATypeParameter_WhenTried_ThenComparesResourceTypeIdViaTheResolver()
    {
        var predicate = new SearchParameterPredicateExpression(TypeParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "Patient", text: null));

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        var equal = result.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("ResourceTypeId");
        equal.Value.Value.ShouldBe((short)103);
    }

    [Fact]
    public void GivenAnIdParameterWithANotModifier_WhenTried_ThenThrowsRatherThanSilentlyDroppingTheNegation()
    {
        // Arrange -- _id:not=123. Without this guard, the modifier would be silently discarded and
        // this would lower to a POSITIVE match (WHERE ResourceId = '123'), the exact opposite of what
        // :not means -- the same bug class Lower.LowerSearchParameter's own :not handling exists to
        // prevent, just reachable here through the resource-column extraction path instead.
        var predicate = new SearchParameterPredicateExpression(IdParameter(), SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new TokenSearchValue(system: null, code: "123", text: null));

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    [Fact]
    public void GivenATypeParameterWithANotModifier_WhenTried_ThenThrows()
    {
        var predicate = new SearchParameterPredicateExpression(TypeParameter(), SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new TokenSearchValue(system: null, code: "Patient", text: null));

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }
}
