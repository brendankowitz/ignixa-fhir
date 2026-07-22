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

    private static SearchParameterPredicateExpression ReferenceComponent(string code, Uri? baseUri = null)
        => new(ComponentParameter(code, SearchParamType.Reference), SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(
                baseUri is null ? ReferenceKind.Internal : ReferenceKind.External,
                baseUri: baseUri!,
                resourceType: "DocumentReference",
                resourceId: "456"));

    private static SearchParameterPredicateExpression TokenComponent(string code, string? system = null)
        => new(ComponentParameter(code, SearchParamType.Token), SearchComparator.Eq, modifier: null,
            new TokenSearchValue(system, code: "replaces", text: null));

    [Fact]
    public void GivenALocalReferenceComponentThenATokenComponent_WhenLowered_ThenBaseUri1IsNullAndTypeIdAndId()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[] { ReferenceComponent("target"), TokenComponent("code") };

        // Act
        var cte = ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55), 55);

        // Assert — (BaseUri1 IS NULL AND TypeId1 = @p0 AND Id1 = @p1) AND Code2 = @p2
        cte.SearchParamId.ShouldBe((short)404);
        cte.ResourceTypeId.ShouldBe((short)55);
        cte.Table.TableName.ShouldBe("ReferenceTokenCompositeSearchParam");

        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();

        // Reference predicate (left)
        var refOuterAnd = outer.Left.ShouldBeOfType<Predicate.And>();
        var refInnerAnd = refOuterAnd.Left.ShouldBeOfType<Predicate.And>();

        // BaseUri1 IS NULL
        var baseUriIsNull = refInnerAnd.Left.ShouldBeOfType<Predicate.IsNull>();
        baseUriIsNull.Column.Column.ShouldBe("BaseUri1");

        // TypeId1 = @p0
        var typePredicate = refInnerAnd.Right.ShouldBeOfType<Predicate.Equal>();
        typePredicate.Column.Column.ShouldBe("ReferenceResourceTypeId1");
        typePredicate.Value.Value.ShouldBe((short)55);

        // Id1 = @p1
        var idPredicate = refOuterAnd.Right.ShouldBeOfType<Predicate.Equal>();
        idPredicate.Column.Column.ShouldBe("ReferenceResourceId1");
        idPredicate.Value.Value.ShouldBe("456");

        // Token predicate (right)
        var tokenPredicate = outer.Right.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code2");
        tokenPredicate.Value.Value.ShouldBe("replaces");
    }

    [Fact]
    public void GivenATokenComponentThenALocalReferenceComponent_WhenLowered_ThenStillFindsRolesByType()
    {
        // Arrange — swapped order proves role assignment is type-based, not positional
        var composite = CompositeParameter();
        var components = new[] { TokenComponent("code"), ReferenceComponent("target") };

        // Act
        var cte = ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55), 55);

        // Assert — identical shape to non-swapped case
        cte.ResourceTypeId.ShouldBe((short)55);
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var refOuterAnd = outer.Left.ShouldBeOfType<Predicate.And>();
        var refInnerAnd = refOuterAnd.Left.ShouldBeOfType<Predicate.And>();

        // BaseUri1 IS NULL
        refInnerAnd.Left.ShouldBeOfType<Predicate.IsNull>().Column.Column.ShouldBe("BaseUri1");
        refInnerAnd.Right.ShouldBeOfType<Predicate.Equal>().Value.Value.ShouldBe((short)55);
        refOuterAnd.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceId1");

        var tokenPredicate = outer.Right.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code2");
        tokenPredicate.Value.Value.ShouldBe("replaces");
    }

    [Fact]
    public void GivenAnExternalReferenceComponent_WhenLowered_ThenBaseUri1EqualCollateBin2AndTypeIdAndId()
    {
        // Arrange
        var composite = CompositeParameter();
        var externalRef = ReferenceComponent("target", baseUri: new Uri("http://example.org/fhir/"));
        var components = new[] { externalRef, TokenComponent("code") };

        // Act
        var cte = ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55), 55);

        // Assert — (BaseUri1 = @p0 COLLATE BIN2 AND TypeId1 = @p1 AND Id1 = @p2) AND Code2 = @p3
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var refOuterAnd = outer.Left.ShouldBeOfType<Predicate.And>();
        var refInnerAnd = refOuterAnd.Left.ShouldBeOfType<Predicate.And>();

        // BaseUri1 = @p0 COLLATE Latin1_General_100_BIN2
        var baseUriEqual = refInnerAnd.Left.ShouldBeOfType<Predicate.Equal>();
        baseUriEqual.Column.Column.ShouldBe("BaseUri1");
        baseUriEqual.Value.Value.ShouldBe("http://example.org/fhir/");
        baseUriEqual.Collation.ShouldBe("Latin1_General_100_BIN2");

        // TypeId1 = @p1
        var typePredicate = refInnerAnd.Right.ShouldBeOfType<Predicate.Equal>();
        typePredicate.Column.Column.ShouldBe("ReferenceResourceTypeId1");
        typePredicate.Value.Value.ShouldBe((short)55);

        // Id1 = @p2
        var idPredicate = refOuterAnd.Right.ShouldBeOfType<Predicate.Equal>();
        idPredicate.Column.Column.ShouldBe("ReferenceResourceId1");
        idPredicate.Value.Value.ShouldBe("456");

        // Token predicate preserved
        var tokenPredicate = outer.Right.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code2");
        tokenPredicate.Value.Value.ShouldBe("replaces");
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenComparesSystemId2AndCode2()
    {
        // Arrange — system|code on the token slot
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
