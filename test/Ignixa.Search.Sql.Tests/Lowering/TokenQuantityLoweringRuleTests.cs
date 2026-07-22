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

public class TokenQuantityLoweringRuleTests
{
    private static LeafContext ContextResolving(
        SearchParameterInfo compositeParameter,
        short searchParamId,
        IReadOnlyDictionary<string, int?>? systemIds = null,
        IReadOnlyDictionary<string, int?>? quantityCodeIds = null)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>(),
            compartmentMembership: null,
            systemIds: systemIds,
            quantityCodeIds: quantityCodeIds));

    private static SearchParameterInfo CompositeParameter()
        => new("component-code-value-quantity", "component-code-value-quantity", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://hl7.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenATokenComponentAndAnUnqualifiedQuantityComponent_WhenLowered_ThenComparesCode1AndLowHighValue2()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 120m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402), 104);

        // Assert
        cte.SearchParamId.ShouldBe((short)402);
        cte.ResourceTypeId.ShouldBe((short)104);
        cte.Table.TableName.ShouldBe("TokenQuantityCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var quantityPredicate = and.Right.ShouldBeOfType<Predicate.And>();
        quantityPredicate.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue2");
        quantityPredicate.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue2");
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenComparesSystemId1AndCode1()
    {
        // Arrange — system|code on the token slot (slot 1)
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var systemIds = new Dictionary<string, int?> { ["http://loinc.org"] = 42 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 120m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402, systemIds), 104);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenAnd = and.Left.ShouldBeOfType<Predicate.And>();
        var systemEqual = tokenAnd.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId1");
        systemEqual.Value.Value.ShouldBe(42);
        var codeEqual = tokenAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code1");
        codeEqual.Value.Value.ShouldBe("8480-6");
    }

    [Fact]
    public void GivenAFullyQualifiedQuantityComponent_WhenLowered_ThenConjoinsNumericAndSystemId2AndQuantityCodeId2Predicates()
    {
        // Arrange — system|code on the quantity slot (slot 2)
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var systemIds = new Dictionary<string, int?> { ["http://unitsofmeasure.org"] = 42 };
        var quantityCodeIds = new Dictionary<string, int?> { ["mg"] = 77 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 120m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402, systemIds, quantityCodeIds), 104);

        // Assert — And(token, And(And(numeric2, Equal(SystemId2)), Equal(QuantityCodeId2)))
        var topAnd = cte.Predicate.ShouldBeOfType<Predicate.And>();
        topAnd.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("Code1");
        var quantityOuter = topAnd.Right.ShouldBeOfType<Predicate.And>();
        var quantityMiddle = quantityOuter.Left.ShouldBeOfType<Predicate.And>();
        var quantityNumeric = quantityMiddle.Left.ShouldBeOfType<Predicate.And>();
        quantityNumeric.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue2");
        quantityNumeric.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue2");
        quantityMiddle.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("SystemId2");
        quantityOuter.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("QuantityCodeId2");
    }

    [Fact]
    public void GivenACodeOnlyQuantityComponent_WhenLowered_ThenConjoinsNumericAndQuantityCodeId2PredicateWithNoSystemIsNullConstraint()
    {
        // Arrange — value||code on the quantity slot: constrains QuantityCodeId2 only; no SystemId2 IS NULL
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var quantityCodeIds = new Dictionary<string, int?> { ["mg"] = 77 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: "mg", 120m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402, quantityCodeIds: quantityCodeIds), 104);

        // Assert — And(token, And(numeric2, Equal(QuantityCodeId2, 77))); Right is Equal not IsNull
        var topAnd = cte.Predicate.ShouldBeOfType<Predicate.And>();
        topAnd.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("Code1");
        var quantityAnd = topAnd.Right.ShouldBeOfType<Predicate.And>();
        var numericAnd = quantityAnd.Left.ShouldBeOfType<Predicate.And>();
        numericAnd.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue2");
        numericAnd.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue2");
        var codeEqual = quantityAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("QuantityCodeId2");
        codeEqual.Value.Value.ShouldBe(77);
    }

    [Fact]
    public void GivenAnUnknownSystemInQuantityComponent_WhenLowered_ThenReturnsFalsePredicateForQuantitySlot()
    {
        // Arrange — non-empty quantity system where resolver returned null (known miss)
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var systemIds = new Dictionary<string, int?> { ["http://unknown.org"] = null };
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unknown.org", code: null!, 120m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402, systemIds), 104);

        // Assert — quantity slot is False; whole composite is And(tokenPredicate, False)
        var topAnd = cte.Predicate.ShouldBeOfType<Predicate.And>();
        topAnd.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("Code1");
        topAnd.Right.ShouldBeOfType<Predicate.False>();
    }

    // :ap composite proof — quantity slot dispatches through NumericRangeComparison.Build (via
    // QuantityColumnPredicate.Build) with the same tolerance formula as the leaf, while a qualified
    // token (slot 1) and fully qualified quantity system/code (slot 2, Phase 1) are retained around it.
    // 5.4m: tol = max(pm=0.05, abs(5.4)*0.10=0.54) = 0.54 → [4.86, 5.94] (same as leaf QuantityLoweringRuleTests).
    [Fact]
    public void GivenApComparatorOnQualifiedQuantityWithQualifiedToken_WhenLowered_ThenWidensRangeAndRetainsSystemAndCodeIdentity()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var systemIds = new Dictionary<string, int?>
        {
            ["http://loinc.org"] = 42,
            ["http://unitsofmeasure.org"] = 43,
        };
        var quantityCodeIds = new Dictionary<string, int?> { ["mg"] = 77 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Ap, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 5.4m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402, systemIds, quantityCodeIds), 104);

        // Assert — And(And(Equal(SystemId1),Equal(Code1)), And(And(And(Ge,Le), Equal(SystemId2)), Equal(QuantityCodeId2)))
        var topAnd = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenAnd = topAnd.Left.ShouldBeOfType<Predicate.And>();
        var systemEqual = tokenAnd.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId1");
        systemEqual.Value.Value.ShouldBe(42);
        var codeEqual = tokenAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code1");
        codeEqual.Value.Value.ShouldBe("8480-6");

        var quantityOuter = topAnd.Right.ShouldBeOfType<Predicate.And>();
        var quantityMiddle = quantityOuter.Left.ShouldBeOfType<Predicate.And>();
        var quantityRange = quantityMiddle.Left.ShouldBeOfType<Predicate.And>();
        var quantityGe = quantityRange.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        quantityGe.Column.Column.ShouldBe("LowValue2");
        quantityGe.Value.Value.ShouldBe(4.86m);
        var quantityLe = quantityRange.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        quantityLe.Column.Column.ShouldBe("HighValue2");
        quantityLe.Value.Value.ShouldBe(5.94m);
        var quantitySystemEqual = quantityMiddle.Right.ShouldBeOfType<Predicate.Equal>();
        quantitySystemEqual.Column.Column.ShouldBe("SystemId2");
        quantitySystemEqual.Value.Value.ShouldBe(43);
        var quantityCodeEqual = quantityOuter.Right.ShouldBeOfType<Predicate.Equal>();
        quantityCodeEqual.Column.Column.ShouldBe("QuantityCodeId2");
        quantityCodeEqual.Value.Value.ShouldBe(77);
    }
}
