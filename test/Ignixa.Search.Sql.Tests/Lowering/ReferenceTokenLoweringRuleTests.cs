using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class ReferenceTokenLoweringRuleTests
{
    private static LeafContext ContextResolving(
        SearchParameterInfo compositeParameter,
        short searchParamId,
        string resourceType,
        short resourceTypeId,
        IReadOnlyDictionary<string, int?>? systemIds = null)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short> { [resourceType] = resourceTypeId },
            compartmentMembership: null,
            systemIds: systemIds));

    private static SearchParameterInfo CompositeParameter()
        => new("relatesto", "relatesto", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/DocumentReference-relatesto"));

    private static SearchParameterInfo ComponentParameter(string code, SearchParamType type)
        => new(code, code, type, new Uri($"http://example.org/fhir/SearchParameter/DocumentReference-{code}"));

    private static SearchParameterPredicateExpression ReferenceComponent(string code)
        => new(ComponentParameter(code, SearchParamType.Reference), SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: "456"));

    private static SearchParameterPredicateExpression TokenComponent(string code, string? system = null)
        => new(ComponentParameter(code, SearchParamType.Token), SearchComparator.Eq, modifier: null,
            new TokenSearchValue(system, code: "replaces", text: null));

    [Fact]
    public void GivenAReferenceComponentThenATokenComponent_WhenLowered_ThenComparesReferenceIdAndCode2()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[] { ReferenceComponent("target"), TokenComponent("code") };

        // Act
        var cte = ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55), 55);

        // Assert
        cte.SearchParamId.ShouldBe((short)404);
        cte.ResourceTypeId.ShouldBe((short)55);
        cte.Table.TableName.ShouldBe("ReferenceTokenCompositeSearchParam");
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var referencePredicate = outer.Left.ShouldBeOfType<Predicate.And>();
        var typePredicate = referencePredicate.Left.ShouldBeOfType<Predicate.Equal>();
        typePredicate.Column.Column.ShouldBe("ReferenceResourceTypeId1");
        typePredicate.Value.Value.ShouldBe((short)55);
        var idPredicate = referencePredicate.Right.ShouldBeOfType<Predicate.Equal>();
        idPredicate.Column.Column.ShouldBe("ReferenceResourceId1");
        idPredicate.Value.Value.ShouldBe("456");
        var tokenPredicate = outer.Right.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code2");
        tokenPredicate.Value.Value.ShouldBe("replaces");
    }

    [Fact]
    public void GivenATokenComponentThenAReferenceComponent_WhenLowered_ThenStillFindsRolesByType()
    {
        // Arrange -- swapped order proves role assignment is type-based, not positional
        // (mirrors RefTokenCompositeRowGenerator's own "find by type, not position" handling of
        // definitions like DocumentReference.relationship that swap component expressions).
        var composite = CompositeParameter();
        var components = new[] { TokenComponent("code"), ReferenceComponent("target") };

        // Act
        var cte = ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55), 55);

        // Assert -- identical shape AND identical bound values to the non-swapped case, proving
        // the reference's id/type land on the reference columns and the token's code lands on
        // Code2 regardless of which array position each component started at.
        cte.ResourceTypeId.ShouldBe((short)55);
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var referencePredicate = outer.Left.ShouldBeOfType<Predicate.And>();
        referencePredicate.Left.ShouldBeOfType<Predicate.Equal>().Value.Value.ShouldBe((short)55);
        var idPredicate = referencePredicate.Right.ShouldBeOfType<Predicate.Equal>();
        idPredicate.Column.Column.ShouldBe("ReferenceResourceId1");
        idPredicate.Value.Value.ShouldBe("456");
        var tokenPredicate = outer.Right.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code2");
        tokenPredicate.Value.Value.ShouldBe("replaces");
    }

    [Fact]
    public void GivenAnAbsoluteReference_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var referenceParam = ComponentParameter("target", SearchParamType.Reference);
        var absoluteReference = new SearchParameterPredicateExpression(
            referenceParam, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.External, baseUri: new Uri("http://example.org/fhir"), resourceType: "DocumentReference", resourceId: "456"));
        var components = new[] { absoluteReference, TokenComponent("code") };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55), 55));
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenComparesSystemId2AndCode2()
    {
        // Arrange — system|code on the token slot (which is slot 2 in ReferenceToken)
        var composite = CompositeParameter();
        var systemIds = new Dictionary<string, int?> { ["http://example.org/relationship-type"] = 88 };
        var components = new[] { ReferenceComponent("target"), TokenComponent("code", system: "http://example.org/relationship-type") };

        // Act
        var cte = ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55, systemIds), 55);

        // Assert
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        // Reference part is still on the left
        outer.Left.ShouldBeOfType<Predicate.And>();
        // Token part is an And of system + code
        var tokenAnd = outer.Right.ShouldBeOfType<Predicate.And>();
        var systemEqual = tokenAnd.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId2");
        systemEqual.Value.Value.ShouldBe(88);
        var codeEqual = tokenAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code2");
        codeEqual.Value.Value.ShouldBe("replaces");
    }
}
